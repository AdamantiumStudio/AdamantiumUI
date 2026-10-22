using System;
using System.Linq;
using Adamantium.UI.Core.Text;
using Adamantium.UI.Platforms.Linux;
using NUnit.Framework;

namespace Adamantium.UITests;

public class EnchantSpellCheckerTests
{
    [Test]
    public void Words_KeepApostrophesAndHyphensInside_AndSkipNumbers()
    {
        const string text = "It's a well-known 3rd try - 'quoted'";

        var words = EnchantSpellChecker.Words(text).Select(r => text.Substring(r.Start, r.Length));

        Assert.That(words, Is.EqualTo(new[] { "It's", "a", "well-known", "try", "quoted" }));
    }

    [Test]
    public void Misspellings_AreFound_AndHaveSuggestions()
    {
        var checker = Checker();
        const string text = "Helo, this wrold is fine.";

        var wrong = checker.Check(text, "en-US").Select(r => text.Substring(r.Start, r.Length));

        Assert.Multiple(() =>
        {
            Assert.That(wrong, Is.EqualTo(new[] { "Helo", "wrold" }));
            Assert.That(checker.Suggest("wrold", "en-US"), Does.Contain("world"));
        });
    }

    [Test]
    public void AnIgnoredWord_IsNoLongerWrong()
    {
        var checker = Checker();
        var word = "adamantiumish" + Environment.ProcessId;

        checker.IgnoreAll(word, "en-US");

        Assert.That(checker.Check(word, "en-US"), Is.Empty);
    }

    [Test]
    public void ALanguage_FallsBackToItsFirstSubtag_OrIsNotSupported()
    {
        var checker = Checker();

        Assert.Multiple(() =>
        {
            Assert.That(checker.Supports("en-US"), Is.True);
            Assert.That(checker.Supports("xx-YY"), Is.False);
        });
    }

    private static EnchantSpellChecker Checker()
    {
        var checker = new EnchantSpellChecker();
        if (!OperatingSystem.IsLinux() || !checker.Supports("en-US"))
        {
            Assert.Ignore("No Enchant with an English dictionary here.");
        }

        return checker;
    }
}
