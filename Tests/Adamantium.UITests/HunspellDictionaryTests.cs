using System.Text;
using Adamantium.UI.Platforms.Linux.Hunspell;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A hunspell dictionary read for completing words, on small .aff and .dic files written as hunspell(5)
/// describes them.</summary>
[TestFixture]
public class HunspellDictionaryTests
{
    private const string English = """
        SET UTF-8
        NEEDAFFIX X

        PFX U Y 1
        PFX U   0     un         .

        SFX S Y 3
        SFX S   y     ies        [^aeiou]y
        SFX S   0     s          [aeiou]y
        SFX S   0     s          [^y]

        SFX D Y 2
        SFX D   0     ed/L       [^e]
        SFX D   0     d/L        e

        SFX L N 1
        SFX L   0     ly         .

        SFX N N 1
        SFX N   0     ness       .
        """;

    [Test]
    public void ASuffix_StripsAndAddsByItsCondition()
    {
        var dictionary = Read(English, "3", "city/S", "day/S", "cat/S");

        Assert.Multiple(() =>
        {
            Assert.That(dictionary.Complete("cit", 5), Is.EqualTo(new[] { "city", "cities" }));
            Assert.That(dictionary.Complete("day", 5), Is.EqualTo(new[] { "day", "days" }));
            Assert.That(dictionary.Complete("cat", 5), Is.EqualTo(new[] { "cat", "cats" }));
        });
    }

    [Test]
    public void APrefix_AndACrossProduct_PutBothOnTheStem()
    {
        var dictionary = Read(English, "1", "do/US");

        Assert.That(dictionary.Complete("und", 5), Is.EqualTo(new[] { "undo", "undos" }));
    }

    [Test]
    public void ASuffixesOwnSuffix_GoesOnItsForm_OneLevel()
    {
        var dictionary = Read(English, "1", "mark/D");

        Assert.That(dictionary.Complete("mar", 5), Is.EqualTo(new[] { "mark", "marked", "markedly" }));
    }

    [Test]
    public void AStemThatNeedsAnAffix_IsNoWordAlone()
    {
        var dictionary = Read(English, "1", "happi/XN");

        Assert.That(dictionary.Complete("hap", 5), Is.EqualTo(new[] { "happiness" }));
    }

    [Test]
    public void ShorterFirst_ThenByTheAlphabet_InAnyCase()
    {
        var dictionary = Read(English, "4", "card", "Carl", "cab", "cart/S");

        Assert.Multiple(() =>
        {
            Assert.That(dictionary.Complete("ca", 3), Is.EqualTo(new[] { "cab", "card", "Carl" }));
            Assert.That(dictionary.Complete("CAR", 5), Is.EqualTo(new[] { "card", "Carl", "cart", "carts" }));
            Assert.That(dictionary.Complete("dog", 5), Is.Empty);
        });
    }

    // Two characters a flag (FLAG long), flag sets by number (AF), a rule with no condition, a slash in a word.
    [Test]
    public void LongFlags_Aliases_ARuleWithNoCondition_AndAnEscapedSlash()
    {
        const string aff = """
            SET UTF-8
            FLAG long
            AF 1
            AF AaBb

            SFX Aa Y 1
            SFX Aa   0     ы

            SFX Bb Y 1
            SFX Bb   0     ам   .
            """;
        var dictionary = Read(aff, "2", "стол/1", "и\\/или");

        Assert.Multiple(() =>
        {
            Assert.That(dictionary.Complete("сто", 5), Is.EqualTo(new[] { "стол", "столы", "столам" }));
            Assert.That(dictionary.Complete("и/", 5), Is.EqualTo(new[] { "и/или" }));
        });
    }

    [Test]
    public void ADictionaryInKoi8R_IsReadInIt()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var koi8 = Encoding.GetEncoding("koi8-r");
        var aff = koi8.GetBytes("SET KOI8-R\nSFX A Y 1\nSFX A 0 а .\n");
        var dic = koi8.GetBytes("1\nдом/A\n");

        var dictionary = HunspellDictionary.Read(aff, dic);

        Assert.That(dictionary.Complete("дом", 5), Is.EqualTo(new[] { "дом", "дома" }));
    }

    private static HunspellDictionary Read(string aff, params string[] dic) =>
        HunspellDictionary.Read(Encoding.UTF8.GetBytes(aff), Encoding.UTF8.GetBytes(string.Join('\n', dic)));
}
