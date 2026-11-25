using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.UI.Core.Text;
using Adamantium.UI.Platforms.Windows.TextPrediction;
using Adamantium.Win32.WinRT;
using Serilog;

namespace Adamantium.UI.Platforms.Windows;

/// <summary>Windows' own text prediction (Windows.Data.Text.TextPredictionGenerator, Windows 10 1803 and later): the
/// typing suggestions that come with the languages the user added to Windows. One generator per language, made on first
/// use. A generator takes any tag and answers nothing for a language it has no suggestions for, so a language is
/// supported when the user has it, matched by its first subtag (ru-RU to the user's ru-UA). The user adding or removing
/// a language (their list in the registry changing) is seen on the next ask. Its objects can be called from any
/// thread; a generator is called and released only under one lock, so none is released during a call on it (an
/// operation it started holds its own reference).</summary>
internal sealed class WindowsWordPredictor : IWordPredictor, IDisposable
{
    private const string GeneratorClass = "Windows.Data.Text.TextPredictionGenerator";

    private readonly object _gate = new();
    private readonly Dictionary<string, ITextPredictionGenerator2> _generators = new(StringComparer.OrdinalIgnoreCase);
    private readonly RegistryKeyWatch _userLanguages;
    private ITextPredictionGeneratorFactory _factory;
    private bool _factoryTried;
    private bool _disposed;

    public WindowsWordPredictor() : this(RegistryKeyWatch.UserLanguagesKey)
    {
    }

    /// <summary>A predictor that takes a change of <paramref name="userLanguagesKey"/> (of the current user) for the
    /// user's languages changing.</summary>
    internal WindowsWordPredictor(string userLanguagesKey) => _userLanguages = new RegistryKeyWatch(userLanguagesKey);

    public bool Supports(string language)
    {
        lock (_gate)
            return FindGenerator(language) != null;
    }

    public Task<IReadOnlyList<string>> CompleteAsync(string language, string start,
        IReadOnlyList<string> previousWords, int count, CancellationToken cancellation) =>
        RequestCandidatesAsync(language, start, TextPredictionOptions.Predictions, previousWords, count, cancellation);

    public Task<IReadOnlyList<string>> CorrectAsync(string language, string word, IReadOnlyList<string> previousWords,
        int count, CancellationToken cancellation) =>
        RequestCandidatesAsync(language, word, TextPredictionOptions.Corrections, previousWords, count, cancellation);

    public Task<IReadOnlyList<string>> PredictNextAsync(string language, IReadOnlyList<string> previousWords,
        int count, CancellationToken cancellation)
    {
        if (count <= 0 || previousWords.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        string[] before = [.. previousWords];
        return AskAsync(() =>
        {
            IStringListOperation operation = null;
            lock (_gate)
            {
                if (FindGenerator(language) is { } generator)
                {
                    ThrowOnFailure(generator.GetNextWordCandidatesAsync((uint)count, new StringIterable(before),
                        out operation));
                }
            }

            return Read(operation, cancellation);
        }, cancellation);
    }

    private Task<IReadOnlyList<string>> RequestCandidatesAsync(string language, string text,
        TextPredictionOptions options, IReadOnlyList<string> previousWords, int count, CancellationToken cancellation)
    {
        if (count <= 0 || string.IsNullOrEmpty(text))
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        string[] before = [.. previousWords];
        return AskAsync(() =>
        {
            IStringListOperation operation = null;
            var input = WinRTInterop.CreateString(text);
            try
            {
                lock (_gate)
                {
                    if (FindGenerator(language) is { } generator)
                    {
                        ThrowOnFailure(generator.GetCandidatesWithParametersAsync(input, (uint)count, options,
                            new StringIterable(before), out operation));
                    }
                }
            }
            finally
            {
                WinRTInterop.DeleteString(input);
            }

            return Read(operation, cancellation);
        }, cancellation);
    }

    /// <summary>What <paramref name="ask"/> answers. Windows' input service now and then fails one ask of many (measured:
    /// 0x87B20803, the others asked with it answered), so a failed one is asked once more; failed again, it is logged and
    /// has no words.</summary>
    internal static async Task<IReadOnlyList<string>> AskAsync(Func<Task<IReadOnlyList<string>>> ask,
        CancellationToken cancellation)
    {
        try
        {
            return await ask().ConfigureAwait(false);
        }
        catch (COMException) when (!cancellation.IsCancellationRequested)
        {
        }
        catch (COMException)
        {
            return [];
        }

        try
        {
            return await ask().ConfigureAwait(false);
        }
        catch (COMException failure)
        {
            // Debug: with the service down, every letter typed asks twice.
            if (!cancellation.IsCancellationRequested)
            {
                Log.Logger.Debug("Windows' text prediction failed twice, the last time with 0x{Error:X8}", failure.ErrorCode);
            }

            return [];
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            ForgetGenerators();
            _userLanguages.Dispose();
            if (_factory != null)
            {
                Marshal.ReleaseComObject(_factory);
                _factory = null;
            }
        }
    }

    // A COMException whatever the code: Marshal's own turns some into other exceptions.
    private static void ThrowOnFailure(int code)
    {
        if (code < 0)
        {
            throw new COMException($"Windows' text prediction failed with 0x{code:X8}.", code);
        }
    }

    private static Task<IReadOnlyList<string>> Read(IStringListOperation operation, CancellationToken cancellation) =>
        operation != null
            ? StringListOperations.ReadAsync(operation, cancellation)
            : Task.FromResult<IReadOnlyList<string>>([]);

    private ITextPredictionGenerator2 FindGenerator(string language)
    {
        // Let go meanwhile - between an ask and its second try - it makes no generator again.
        if (_disposed || string.IsNullOrEmpty(language))
        {
            return null;
        }

        if (_userLanguages.TakeChange())
        {
            ForgetGenerators();
        }

        if (_generators.TryGetValue(language, out var known))
        {
            return known;
        }

        var made = CreateGenerator(language);
        _generators[language] = made;
        return made;
    }

    private void ForgetGenerators()
    {
        foreach (var generator in _generators.Values)
        {
            if (generator != null)
            {
                Marshal.ReleaseComObject(generator);
            }
        }

        _generators.Clear();
    }

    private ITextPredictionGenerator2 CreateGenerator(string language)
    {
        if (!_factoryTried)
        {
            _factoryTried = true;
            _factory = WinRTInterop.GetActivationFactory<ITextPredictionGeneratorFactory>(GeneratorClass);
        }

        if (_factory == null || !IsUserLanguage(language))
        {
            return null;
        }

        var tag = WinRTInterop.CreateString(language);
        try
        {
            if (_factory.Create(tag, out var generator) != 0 || generator == null)
            {
                return null;
            }

            if (generator.GetLanguageAvailableButNotInstalled(out var notInstalled) == 0 && !notInstalled
                && generator is ITextPredictionGenerator2 usable)
            {
                return usable;
            }

            Marshal.ReleaseComObject(generator);
            return null;
        }
        finally
        {
            WinRTInterop.DeleteString(tag);
        }
    }

    private static bool IsUserLanguage(string language)
    {
        var wanted = WindowsSpellChecker.Primary(language);
        foreach (var userLanguage in WindowsUserLanguages.Read())
        {
            if (WindowsSpellChecker.Primary(userLanguage) == wanted)
            {
                return true;
            }
        }

        return false;
    }
}
