using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.MacOS.AppKit;
using Adamantium.MacOS.ObjectiveC;
using Adamantium.UI.Core.Text;

namespace Adamantium.UI.Platforms.MacOS;

/// <summary>The system's spelling and word completion: the dictionaries of its languages and the user's own words. A
/// language is matched by its tag, then by its first subtag. Called from any thread; one at a time. The system tells no
/// next word.</summary>
internal sealed class MacOSSpelling : ISpellChecker, IWordPredictor
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, string> _matched = new(StringComparer.OrdinalIgnoreCase);
    private string[] _languages;
    private nint _document;

    public bool Supports(string language) => Ask(language, _ => true, false);

    public IReadOnlyList<TextRange> Check(string text, string language)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        return Ask<IReadOnlyList<TextRange>>(language, native =>
        {
            List<TextRange> found = [];
            for (var from = 0; from < text.Length;)
            {
                var (start, length) = NSSpellChecker.FindMisspelled(text, from, native, _document);
                // It starts over at the end.
                if (length == 0 || start < from)
                    break;

                found.Add(new TextRange(start, length));
                from = start + length;
            }

            return found;
        }, []);
    }

    public IReadOnlyList<string> Suggest(string word, string language) => string.IsNullOrEmpty(word)
        ? []
        : Ask<IReadOnlyList<string>>(language, native => NSSpellChecker.Guess(word, native, _document), []);

    public void IgnoreAll(string word, string language)
    {
        if (!string.IsNullOrEmpty(word))
            InPool(() => NSSpellChecker.Ignore(word, _document));
    }

    public void AddToDictionary(string word, string language)
    {
        if (!string.IsNullOrEmpty(word))
            InPool(() => NSSpellChecker.Learn(word));
    }

    public Task<IReadOnlyList<string>> CompleteAsync(string language, string start, IReadOnlyList<string> previousWords,
        int count, CancellationToken cancellation) => string.IsNullOrEmpty(start) || count <= 0
        ? Task.FromResult<IReadOnlyList<string>>([])
        : Task.Run(() => Ask<IReadOnlyList<string>>(language,
            native => NSSpellChecker.Complete(start, native, _document).Take(count).ToList(), []), cancellation);

    public Task<IReadOnlyList<string>> CorrectAsync(string language, string word, IReadOnlyList<string> previousWords,
        int count, CancellationToken cancellation) => string.IsNullOrEmpty(word) || count <= 0
        ? Task.FromResult<IReadOnlyList<string>>([])
        : Task.Run(() => (IReadOnlyList<string>)Suggest(word, language).Take(count).ToList(), cancellation);

    public Task<IReadOnlyList<string>> PredictNextAsync(string language, IReadOnlyList<string> previousWords, int count,
        CancellationToken cancellation) => Task.FromResult<IReadOnlyList<string>>([]);

    private T Ask<T>(string language, Func<string, T> ask, T none)
    {
        var answer = none;
        InPool(() =>
        {
            if (Match(language) is { } native)
                answer = ask(native);
        });
        return answer;
    }

    private void InPool(Action action)
    {
        lock (_gate)
        {
            var pool = ObjCRuntime.PushAutoreleasePool();
            try
            {
                if (_document == 0)
                    _document = NSSpellChecker.CreateDocument();
                action();
            }
            finally
            {
                ObjCRuntime.PopAutoreleasePool(pool);
            }
        }
    }

    private string Match(string language)
    {
        if (string.IsNullOrEmpty(language))
            return null;

        if (_matched.TryGetValue(language, out var known))
            return known;

        _languages ??= NSSpellChecker.Languages;
        var tag = language.Replace('-', '_');
        var primary = tag.Split('_')[0];
        var match = Find(name => name.Equals(tag, StringComparison.OrdinalIgnoreCase)) ??
                    Find(name => name.Equals(primary, StringComparison.OrdinalIgnoreCase)) ??
                    Find(name => name.StartsWith(primary + "_", StringComparison.OrdinalIgnoreCase));
        _matched[language] = match;
        return match;
    }

    private string Find(Predicate<string> predicate) => Array.Find(_languages, predicate);
}
