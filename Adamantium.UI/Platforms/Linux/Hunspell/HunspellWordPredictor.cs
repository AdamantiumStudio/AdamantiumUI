using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.UI.Core.Text;

namespace Adamantium.UI.Platforms.Linux.Hunspell;

/// <summary>Word prediction on Linux, which has no system predictor: words a beginning goes on to from the hunspell
/// dictionaries installed - the ones Enchant checks spelling with - and what a misspelled word was meant as from the
/// spell checker. The dictionaries have no frequencies, so shorter words come first; there is no next word. A language
/// is supported when a dictionary for it is installed, matched by its tag, then by its first subtag. A dictionary is
/// looked for the first time its language is asked - found or not, the answer is kept - and read then, off the asking
/// thread.</summary>
internal sealed class HunspellWordPredictor : IWordPredictor
{
    private readonly object _gate = new();
    private readonly ISpellChecker _spelling;
    private readonly IReadOnlyList<string> _folders;
    private readonly Dictionary<string, Task<HunspellDictionary>> _dictionaries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<HunspellDictionary>> _loads = new(StringComparer.Ordinal);

    public HunspellWordPredictor(ISpellChecker spelling) : this(spelling, DictionaryFolders())
    {
    }

    /// <summary>A predictor that reads its dictionaries from <paramref name="folders"/>, the first that has one winning.
    /// </summary>
    internal HunspellWordPredictor(ISpellChecker spelling, IReadOnlyList<string> folders)
    {
        _spelling = spelling;
        _folders = folders;
    }

    public bool Supports(string language) => DictionaryOf(language) != null;

    public async Task<IReadOnlyList<string>> CompleteAsync(string language, string start,
        IReadOnlyList<string> previousWords, int count, CancellationToken cancellation)
    {
        if (string.IsNullOrEmpty(start) || count <= 0 || DictionaryOf(language) is not { } loading)
            return [];

        var dictionary = await loading.WaitAsync(cancellation).ConfigureAwait(false);
        // Off the asking thread even when the dictionary was read already: expanding stems takes milliseconds.
        return dictionary == null ? [] : await Task.Run(() => dictionary.Complete(start, count), cancellation).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<string>> CorrectAsync(string language, string word, IReadOnlyList<string> previousWords,
        int count, CancellationToken cancellation)
    {
        if (string.IsNullOrEmpty(word) || count <= 0 || _spelling == null)
            return Task.FromResult<IReadOnlyList<string>>([]);

        return Task.Run(() => (IReadOnlyList<string>)_spelling.Suggest(word, language).Take(count).ToList(), cancellation);
    }

    public Task<IReadOnlyList<string>> PredictNextAsync(string language, IReadOnlyList<string> previousWords, int count,
        CancellationToken cancellation) => Task.FromResult<IReadOnlyList<string>>([]);

    private Task<HunspellDictionary> DictionaryOf(string language)
    {
        if (string.IsNullOrEmpty(language))
            return null;

        lock (_gate)
        {
            if (_dictionaries.TryGetValue(language, out var known))
                return known;

            // Two tags falling to the same files - en-US and en-GB to en_US.dic - share one dictionary read once.
            var files = FindFiles(language);
            Task<HunspellDictionary> loading = null;
            if (files != null && !_loads.TryGetValue(files.Dic, out loading))
            {
                loading = Task.Run(() => Load(files));
                _loads[files.Dic] = loading;
            }

            _dictionaries[language] = loading;
            return loading;
        }
    }

    private static HunspellDictionary Load(DictionaryFiles files)
    {
        try
        {
            return HunspellDictionary.Load(files.Aff, files.Dic);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Serilog.Log.Logger.Warning("The dictionary {File} could not be read: {Reason}", files.Dic, e.Message);
            return null;
        }
    }

    private DictionaryFiles FindFiles(string language)
    {
        if (string.IsNullOrEmpty(language))
            return null;

        // ru-RU is ru_RU.dic; failing that ru.dic, or any ru_*.dic.
        var tag = language.Replace('-', '_');
        var primary = tag.Split('_')[0];
        foreach (var name in new[] { tag, primary })
        {
            foreach (var folder in _folders)
            {
                var dic = Path.Combine(folder, name + ".dic");
                var aff = Path.ChangeExtension(dic, ".aff");
                if (File.Exists(dic) && File.Exists(aff))
                    return new DictionaryFiles(aff, dic);
            }
        }

        foreach (var folder in _folders.Where(Directory.Exists))
        {
            foreach (var dic in Directory.EnumerateFiles(folder, primary + "_*.dic").Order(StringComparer.Ordinal))
            {
                var aff = Path.ChangeExtension(dic, ".aff");
                if (File.Exists(aff))
                    return new DictionaryFiles(aff, dic);
            }
        }

        return null;
    }

    private static List<string> DictionaryFolders()
    {
        // Where hunspell and Enchant look: DICPATH first, then the user's, then the system's.
        List<string> folders = [];
        if (Environment.GetEnvironmentVariable("DICPATH") is { Length: > 0 } dicPath)
            folders.AddRange(dicPath.Split(':', StringSplitOptions.RemoveEmptyEntries));

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (home.Length > 0)
            folders.Add(Path.Combine(home, ".local", "share", "hunspell"));

        folders.AddRange(["/usr/local/share/hunspell", "/usr/share/hunspell", "/usr/share/myspell",
            "/usr/share/myspell/dicts"]);
        return folders;
    }

    private sealed record DictionaryFiles(string Aff, string Dic);
}
