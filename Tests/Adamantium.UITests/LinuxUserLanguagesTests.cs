using System.Collections.Generic;
using Adamantium.UI.Platforms.Linux;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>The user's languages on Linux: GNOME's input sources taken for the languages they type, in the user's
/// order; the session's languages where the desktop says none.</summary>
[TestFixture]
public class LinuxUserLanguagesTests
{
    [Test]
    public void InputSources_GiveTheirLanguages_InTheUsersOrder()
    {
        List<object> sources =
        [
            new object[] { "xkb", "ru" },
            new object[] { "xkb", "us+intl" },
            new object[] { "ibus", "anthy" },
            new object[] { "xkb", "br" },
        ];

        Assert.That(LinuxUserLanguages.FromInputSources(sources), Is.EqualTo(new[] { "ru-RU", "en-US", "ja-JP", "pt-BR" }));
    }

    [Test]
    public void ASourceOfNoKnownLanguage_OrALanguageTwice_IsLeftOut()
    {
        List<object> sources =
        [
            new object[] { "xkb", "us" },
            new object[] { "xkb", "us+dvorak" },
            new object[] { "xkb", "nowhere" },
            new object[] { "other", "ru" },
            "not a pair",
        ];

        Assert.That(LinuxUserLanguages.FromInputSources(sources), Is.EqualTo(new[] { "en-US" }));
    }

    [Test]
    public void TheSessionsLanguage_ComesFirst_TheRestInTheSourcesOrder()
    {
        // A Russian install: English switched to first, Russian the language the user works in.
        var ordered = LinuxUserLanguages.PreferredFirst(["en-US", "de-DE", "ru-RU"], ["ru-RU"]);

        Assert.That(ordered, Is.EqualTo(new[] { "ru-RU", "en-US", "de-DE" }));
    }

    [Test]
    public void ASessionLanguage_MatchesItsSource_ByTheFirstSubtag()
    {
        var ordered = LinuxUserLanguages.PreferredFirst(["en-US", "pt-BR", "uk-UA"], ["uk", "pt-PT", "fr-FR"]);

        Assert.That(ordered, Is.EqualTo(new[] { "uk-UA", "pt-BR", "en-US" }));
    }

    [Test]
    public void NoSources_GiveNoLanguages() => Assert.That(LinuxUserLanguages.FromInputSources(null), Is.Empty);

    [Test]
    public void TheSessionsLanguages_ComeFromLanguage_InItsOrder()
    {
        var variables = new Dictionary<string, string> { ["LANGUAGE"] = "uk:ru_RU:en_US.UTF-8", ["LANG"] = "de_DE.UTF-8" };

        Assert.That(LinuxUserLanguages.FromEnvironment(variables.GetValueOrDefault),
            Is.EqualTo(new[] { "uk", "ru-RU", "en-US" }));
    }

    [TestCase("ru_RU.UTF-8", "ru-RU")]
    [TestCase("sr_RS@latin", "sr-RS")]
    [TestCase("fr_CA", "fr-CA")]
    public void WithoutLanguage_TheLocaleOfMessages_IsTheLanguage(string locale, string language)
    {
        var variables = new Dictionary<string, string> { ["LANG"] = locale };

        Assert.That(LinuxUserLanguages.FromEnvironment(variables.GetValueOrDefault), Is.EqualTo(new[] { language }));
    }

    [Test]
    public void UnderTheCLocale_LanguageIsNotRead_AsGettextDoesNot()
    {
        var variables = new Dictionary<string, string> { ["LANGUAGE"] = "ru", ["LANG"] = "C.UTF-8" };

        Assert.That(LinuxUserLanguages.FromEnvironment(variables.GetValueOrDefault), Is.Empty);
    }

    [Test]
    public void TheCLocale_IsNoLanguage()
    {
        var variables = new Dictionary<string, string> { ["LC_ALL"] = "C", ["LANG"] = "ru_RU.UTF-8" };

        Assert.That(LinuxUserLanguages.FromEnvironment(variables.GetValueOrDefault), Is.Empty);
    }
}
