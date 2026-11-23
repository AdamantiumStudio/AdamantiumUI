using System;
using System.Collections.Generic;

namespace Adamantium.UI.Platforms.Linux;

/// <summary>The user's languages on a Linux desktop: the input sources they added (GNOME's
/// <c>org.gnome.desktop.input-sources</c>), each xkb layout or input method taken for the language it types, the
/// session's own languages first; where the desktop does not say, the languages of the session (<c>LANGUAGE</c>, then
/// the locale).</summary>
internal static class LinuxUserLanguages
{
    // An xkb layout is named by its country more often than by its language: «us», «br», «ua».
    private static readonly Dictionary<string, string> LanguageOfLayout = new(StringComparer.OrdinalIgnoreCase)
    {
        ["us"] = "en-US", ["gb"] = "en-GB", ["ie"] = "en-IE", ["au"] = "en-AU",
        ["ru"] = "ru-RU", ["ua"] = "uk-UA", ["by"] = "be-BY", ["kz"] = "kk-KZ",
        ["de"] = "de-DE", ["at"] = "de-AT", ["ch"] = "de-CH",
        ["fr"] = "fr-FR", ["be"] = "fr-BE", ["ca"] = "fr-CA",
        ["es"] = "es-ES", ["latam"] = "es-419", ["it"] = "it-IT", ["pt"] = "pt-PT", ["br"] = "pt-BR",
        ["pl"] = "pl-PL", ["cz"] = "cs-CZ", ["sk"] = "sk-SK", ["hu"] = "hu-HU", ["ro"] = "ro-RO",
        ["bg"] = "bg-BG", ["rs"] = "sr-RS", ["hr"] = "hr-HR", ["si"] = "sl-SI",
        ["nl"] = "nl-NL", ["se"] = "sv-SE", ["no"] = "nb-NO", ["dk"] = "da-DK", ["fi"] = "fi-FI",
        ["ee"] = "et-EE", ["lv"] = "lv-LV", ["lt"] = "lt-LT", ["gr"] = "el-GR", ["tr"] = "tr-TR",
        ["il"] = "he-IL", ["ara"] = "ar", ["ir"] = "fa-IR",
        ["jp"] = "ja-JP", ["cn"] = "zh-CN", ["tw"] = "zh-TW", ["kr"] = "ko-KR",
        ["in"] = "hi-IN", ["th"] = "th-TH", ["vn"] = "vi-VN",
    };

    // An input method (IBus) types the language it composes.
    private static readonly Dictionary<string, string> LanguageOfInputMethod = new(StringComparer.OrdinalIgnoreCase)
    {
        ["anthy"] = "ja-JP", ["mozc-jp"] = "ja-JP", ["kkc"] = "ja-JP",
        ["pinyin"] = "zh-CN", ["libpinyin"] = "zh-CN", ["rime"] = "zh-CN", ["chewing"] = "zh-TW",
        ["hangul"] = "ko-KR",
    };

    /// <summary>The languages of GNOME's input sources, an array of (kind, name) pairs - («xkb», «us+intl»),
    /// («ibus», «anthy»); a source of no known language is left out, as is a language twice.</summary>
    public static List<string> FromInputSources(object sources)
    {
        List<string> languages = [];
        if (sources is not IEnumerable<object> pairs)
        {
            return languages;
        }

        foreach (var pair in pairs)
        {
            if (pair is not object[] { Length: 2 } fields || fields[0] is not string kind || fields[1] is not string name)
            {
                continue;
            }

            var language = kind switch
            {
                // A layout's variant follows a «+»: «us+intl» is still the US layout.
                "xkb" => LanguageOfLayout.GetValueOrDefault(name.Split('+')[0]),
                "ibus" => LanguageOfInputMethod.GetValueOrDefault(name.Split(':')[0]),
                _ => null,
            };
            if (language != null && !languages.Contains(language))
            {
                languages.Add(language);
            }
        }

        return languages;
    }

    /// <summary>The session's languages, as gettext takes them: <c>LANGUAGE</c> («ru:en_US»), else the locale of
    /// messages (<c>LC_ALL</c>, <c>LC_MESSAGES</c>, <c>LANG</c>: «ru_RU.UTF-8»); none for the C locale, whose
    /// <c>LANGUAGE</c> gettext does not read either.</summary>
    public static List<string> FromEnvironment(Func<string, string> variable)
    {
        List<string> languages = [];
        var locale = variable("LC_ALL") is { Length: > 0 } all ? all
            : variable("LC_MESSAGES") is { Length: > 0 } messages ? messages
            : variable("LANG");
        Add(languages, locale);
        if (languages.Count == 0)
        {
            return languages;
        }

        List<string> listed = [];
        foreach (var entry in (variable("LANGUAGE") ?? string.Empty).Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            Add(listed, entry);
        }

        return listed.Count > 0 ? listed : languages;
    }

    /// <summary>The input sources' languages with the session's own first: the order of the sources is the order the
    /// user switches in, while the session's languages are the ones they chose to work in - a Russian install lists
    /// «us» before «ru». A session language is matched to a source by its first subtag (ru to ru-RU); the rest keep the
    /// sources' order.</summary>
    public static List<string> PreferredFirst(IReadOnlyList<string> sources, IReadOnlyList<string> session)
    {
        List<string> ordered = [];
        foreach (var wanted in session)
        {
            foreach (var source in sources)
            {
                if (!ordered.Contains(source) && string.Equals(Primary(source), Primary(wanted), StringComparison.OrdinalIgnoreCase))
                {
                    ordered.Add(source);
                }
            }
        }

        foreach (var source in sources)
        {
            if (!ordered.Contains(source))
            {
                ordered.Add(source);
            }
        }

        return ordered;
    }

    private static string Primary(string language)
    {
        var end = language.IndexOf('-');
        return end < 0 ? language : language[..end];
    }

    // «ru_RU.UTF-8@euro» is ru-RU; C and POSIX are no language.
    private static void Add(List<string> languages, string locale)
    {
        if (string.IsNullOrEmpty(locale))
        {
            return;
        }

        var end = locale.IndexOfAny(['.', '@']);
        var name = (end < 0 ? locale : locale[..end]).Replace('_', '-');
        if (name.Length > 0 && name is not ("C" or "POSIX") && !languages.Contains(name))
        {
            languages.Add(name);
        }
    }
}
