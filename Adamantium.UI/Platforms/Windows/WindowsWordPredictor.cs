using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.UI.Core.Text;
using Adamantium.UI.Platforms.Windows.TextPrediction;
using Adamantium.Win32.WinRT;

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
    private const string PreferencesClass = "Windows.System.UserProfile.GlobalizationPreferences";
    private const string UserLanguagesKey = @"Control Panel\International\User Profile";

    private readonly object _gate = new();
    private readonly Dictionary<string, ITextPredictionGenerator2> _generators = new(StringComparer.OrdinalIgnoreCase);
    private readonly RegistryKeyWatch _userLanguages;
    private ITextPredictionGeneratorFactory _factory;
    private bool _factoryTried;

    public WindowsWordPredictor() : this(UserLanguagesKey)
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

        IStringListOperation operation = null;
        lock (_gate)
        {
            if (FindGenerator(language) is { } generator
                && generator.GetNextWordCandidatesAsync((uint)count, new StringIterable([.. previousWords]), out operation) != 0)
            {
                operation = null;
            }
        }

        return Read(operation, cancellation);
    }

    private Task<IReadOnlyList<string>> RequestCandidatesAsync(string language, string text,
        TextPredictionOptions options, IReadOnlyList<string> previousWords, int count, CancellationToken cancellation)
    {
        if (count <= 0 || string.IsNullOrEmpty(text))
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        IStringListOperation operation = null;
        var input = WinRTInterop.CreateString(text);
        try
        {
            lock (_gate)
            {
                if (FindGenerator(language) is { } generator
                    && generator.GetCandidatesWithParametersAsync(input, (uint)count, options,
                        new StringIterable([.. previousWords]), out operation) != 0)
                {
                    operation = null;
                }
            }
        }
        finally
        {
            WinRTInterop.DeleteString(input);
        }

        return Read(operation, cancellation);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            ForgetGenerators();
            _userLanguages.Dispose();
            if (_factory != null)
            {
                Marshal.ReleaseComObject(_factory);
                _factory = null;
            }
        }
    }

    private static Task<IReadOnlyList<string>> Read(IStringListOperation operation, CancellationToken cancellation) =>
        operation != null
            ? StringListOperations.ReadAsync(operation, cancellation)
            : Task.FromResult<IReadOnlyList<string>>([]);

    private ITextPredictionGenerator2 FindGenerator(string language)
    {
        if (string.IsNullOrEmpty(language))
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
        foreach (var userLanguage in ReadUserLanguages())
        {
            if (WindowsSpellChecker.Primary(userLanguage) == wanted)
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> ReadUserLanguages()
    {
        List<string> found = [];
        var preferences = WinRTInterop.GetActivationFactory<IGlobalizationPreferencesStatics>(PreferencesClass);
        if (preferences == null)
        {
            return found;
        }

        IStringVectorView languages = null;
        try
        {
            if (preferences.GetLanguages(out languages) != 0 || languages == null || languages.GetSize(out var size) != 0)
            {
                return found;
            }

            for (uint index = 0; index < size; index++)
            {
                if (languages.GetAt(index, out var item) != 0)
                {
                    continue;
                }

                try
                {
                    found.Add(WinRTInterop.ReadString(item));
                }
                finally
                {
                    WinRTInterop.DeleteString(item);
                }
            }

            return found;
        }
        finally
        {
            if (languages != null)
            {
                Marshal.ReleaseComObject(languages);
            }

            Marshal.ReleaseComObject(preferences);
        }
    }
}
