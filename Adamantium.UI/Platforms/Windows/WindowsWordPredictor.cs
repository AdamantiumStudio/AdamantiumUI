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
/// supported when the user has it, matched by its first subtag (ru-RU to the user's ru-UA). Its objects can be called
/// from any thread.</summary>
internal sealed class WindowsWordPredictor : IWordPredictor
{
    private const string GeneratorClass = "Windows.Data.Text.TextPredictionGenerator";
    private const string PreferencesClass = "Windows.System.UserProfile.GlobalizationPreferences";

    private readonly object _gate = new();
    private readonly Dictionary<string, ITextPredictionGenerator2> _generators = new(StringComparer.OrdinalIgnoreCase);
    private ITextPredictionGeneratorFactory _factory;
    private bool _factoryTried;

    public bool Supports(string language) => FindGenerator(language) != null;

    public Task<IReadOnlyList<string>> CompleteAsync(string language, string start,
        IReadOnlyList<string> previousWords, int count, CancellationToken cancellation) =>
        RequestCandidatesAsync(language, start, TextPredictionOptions.Predictions, previousWords, count, cancellation);

    public Task<IReadOnlyList<string>> CorrectAsync(string language, string word, IReadOnlyList<string> previousWords,
        int count, CancellationToken cancellation) =>
        RequestCandidatesAsync(language, word, TextPredictionOptions.Corrections, previousWords, count, cancellation);

    public Task<IReadOnlyList<string>> PredictNextAsync(string language, IReadOnlyList<string> previousWords,
        int count, CancellationToken cancellation)
    {
        if (count <= 0 || previousWords.Count == 0 || FindGenerator(language) is not { } generator)
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        return generator.GetNextWordCandidatesAsync((uint)count, new StringIterable([.. previousWords]), out var operation) == 0
               && operation != null
            ? StringListOperations.ReadAsync(operation, cancellation)
            : Task.FromResult<IReadOnlyList<string>>([]);
    }

    private Task<IReadOnlyList<string>> RequestCandidatesAsync(string language, string text,
        TextPredictionOptions options, IReadOnlyList<string> previousWords, int count, CancellationToken cancellation)
    {
        if (count <= 0 || string.IsNullOrEmpty(text) || FindGenerator(language) is not { } generator)
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        var input = WinRTInterop.CreateString(text);
        try
        {
            return generator.GetCandidatesWithParametersAsync(input, (uint)count, options,
                       new StringIterable([.. previousWords]), out var operation) == 0 && operation != null
                ? StringListOperations.ReadAsync(operation, cancellation)
                : Task.FromResult<IReadOnlyList<string>>([]);
        }
        finally
        {
            WinRTInterop.DeleteString(input);
        }
    }

    private ITextPredictionGenerator2 FindGenerator(string language)
    {
        if (string.IsNullOrEmpty(language))
        {
            return null;
        }

        lock (_gate)
        {
            if (_generators.TryGetValue(language, out var known))
            {
                return known;
            }

            var made = CreateGenerator(language);
            _generators[language] = made;
            return made;
        }
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
