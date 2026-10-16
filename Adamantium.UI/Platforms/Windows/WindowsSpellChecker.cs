using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.UI.Core.Text;
using Adamantium.UI.Platforms.Windows.Spelling;
using Adamantium.Win32;
using NativeChecker = Adamantium.UI.Platforms.Windows.Spelling.ISpellChecker;

namespace Adamantium.UI.Platforms.Windows;

/// <summary>Windows' own spelling (Windows 8 and later): the dictionaries of the languages installed and the user's
/// dictionary. A language is matched to one the system has by its tag, then by its first subtag; a word ignored is
/// ignored for every tag of its language. Its objects live in the multithreaded apartment, so it is called from any
/// thread; a repeated word is no misspelling here.</summary>
internal sealed class WindowsSpellChecker : Core.Text.ISpellChecker
{
    private static readonly Guid FactoryClass = new("7AB36653-1796-484B-BDFA-E74F1DB7C1DC");
    private static readonly Guid FactoryInterface = typeof(ISpellCheckerFactory).GUID;

    private readonly object _gate = new();
    private readonly Dictionary<string, string> _matched = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, NativeChecker> _checkers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<(string Language, string Word)> _ignored = [];
    private ISpellCheckerFactory _factory;
    private bool _factoryTried;
    private string[] _supported;

    public bool Supports(string language) => InApartment(() => CheckerFor(language) != null);

    [SupportedOSPlatform("windows")]
    public IReadOnlyList<TextRange> Check(string text, string language) => InApartment(() =>
    {
        if (string.IsNullOrEmpty(text) || CheckerFor(language) is not { } checker
            || checker.Check(text, out var errors) != 0 || errors == null)
        {
            return (IReadOnlyList<TextRange>)[];
        }

        List<TextRange> found = [];
        try
        {
            while (errors.Next(out var error) == 0 && error != null)
            {
                if (error.GetStartIndex(out var start) == 0 && error.GetLength(out var length) == 0 && length > 0
                    && (error.GetCorrectiveAction(out var action) != 0 || action != CorrectiveAction.Delete)
                    && start + length <= text.Length
                    && !Ignored(language, text.Substring((int)start, (int)length)))
                {
                    found.Add(new TextRange((int)start, (int)length));
                }

                Marshal.ReleaseComObject(error);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(errors);
        }

        return found;
    });

    [SupportedOSPlatform("windows")]
    public IReadOnlyList<string> Suggest(string word, string language) => InApartment(() =>
    {
        if (string.IsNullOrEmpty(word) || CheckerFor(language) is not { } checker
            || checker.Suggest(word, out var suggestions) != 0 || suggestions == null)
        {
            return (IReadOnlyList<string>)[];
        }

        try
        {
            return Strings(suggestions);
        }
        finally
        {
            Marshal.ReleaseComObject(suggestions);
        }
    });

    public void IgnoreAll(string word, string language) => InApartment(() =>
    {
        if (!string.IsNullOrEmpty(word))
        {
            lock (_gate)
            {
                _ignored.Add((Primary(language), word));
            }

            CheckerFor(language)?.Ignore(word);
        }

        return true;
    });

    private bool Ignored(string language, string word)
    {
        lock (_gate)
        {
            return _ignored.Contains((Primary(language), word));
        }
    }

    private static string Primary(string language)
    {
        var tag = language?.Trim() ?? string.Empty;
        var dash = tag.IndexOf('-');
        return (dash > 0 ? tag[..dash] : tag).ToLowerInvariant();
    }

    public void AddToDictionary(string word, string language) => InApartment(() =>
    {
        if (!string.IsNullOrEmpty(word))
        {
            CheckerFor(language)?.Add(word);
        }

        return true;
    });

    private static T InApartment<T>(Func<T> call) =>
        Thread.CurrentThread.GetApartmentState() == ApartmentState.MTA ? call() : Task.Run(call).GetAwaiter().GetResult();

    private NativeChecker CheckerFor(string language)
    {
        var tag = language?.Trim();
        lock (_gate)
        {
            if (string.IsNullOrEmpty(tag) || Factory() is not { } factory)
            {
                return null;
            }

            if (!_matched.TryGetValue(tag, out var supported))
            {
                supported = Matching(factory, tag);
                _matched[tag] = supported;
            }

            if (supported == null)
            {
                return null;
            }

            if (!_checkers.TryGetValue(supported, out var checker))
            {
                checker = factory.CreateSpellChecker(supported, out var created) == 0 ? created : null;
                _checkers[supported] = checker;
            }

            return checker;
        }
    }

    private ISpellCheckerFactory Factory()
    {
        if (!_factoryTried)
        {
            _factoryTried = true;
            var clsid = FactoryClass;
            var iid = FactoryInterface;
            if (Win32Interop.CoCreateInstance(ref clsid, IntPtr.Zero, Win32Interop.ClsCtxInprocServer, ref iid,
                    out var instance) == 0)
            {
                _factory = instance as ISpellCheckerFactory;
            }
        }

        return _factory;
    }

    private string Matching(ISpellCheckerFactory factory, string tag)
    {
        if (_supported == null)
        {
            _supported = factory.GetSupportedLanguages(out var languages) == 0 && languages != null
                ? [.. Strings(languages)]
                : [];
        }

        foreach (var candidate in _supported)
        {
            if (candidate.Equals(tag, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        var dash = tag.IndexOf('-');
        var primary = dash > 0 ? tag[..dash] : tag;
        foreach (var candidate in _supported)
        {
            if (candidate.Equals(primary, StringComparison.OrdinalIgnoreCase)
                || candidate.StartsWith(primary + "-", StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return factory.IsSupported(tag, out var supported) == 0 && supported != 0 ? tag : null;
    }

    private static List<string> Strings(IEnumString strings)
    {
        List<string> read = [];
        var one = new string[1];
        while (strings.Next(1, one, IntPtr.Zero) == 0)
        {
            read.Add(one[0]);
        }

        return read;
    }
}
