using System;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.UI.Core.Media;
using NUnit.Framework;
using FontFamily = Adamantium.UI.Core.Media.FontFamily;
using FontStyle = Adamantium.Fonts.FontStyle;
using FontWeight = Adamantium.Fonts.FontWeight;
using FontStretch = Adamantium.Fonts.FontStretch;

namespace Adamantium.UITests;

/// <summary>Axis values of a variable font for text: parsed from markup, laid over the ones weight, width and style set,
/// and leaving the optical size to the text's size unless it is given.</summary>
public class FontVariationListTests
{
    private static FontFamily RobotoFlex() =>
        new(new Uri(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "RobotoFlex-Variable.ttf")));

    [Test]
    public void Parse_ReadsTagsAndValues_AndOpticalSizeAuto()
    {
        var list = FontVariationList.Parse("wght=650, GRAD=-50.5, opsz=auto");

        Assert.That(list.Select(v => FormattableString.Invariant($"{v.Tag}={v.Value}")),
            Is.EqualTo(new[] { "wght=650", "GRAD=-50.5" }));
    }

    [TestCase("wgt=650")]
    [TestCase("wght=heavy")]
    [TestCase("wght")]
    public void Parse_RefusesWhatIsNotAnAxisAndValue(string text)
    {
        Assert.Throws<FormatException>(() => FontVariationList.Parse(text));
    }

    [Test]
    public void Apply_LaysItsValuesOverTheWeight_AndLeavesTheOpticalSizeToTheText()
    {
        var bold = RobotoFlex().GetFont(new FontWeight(700), FontStyle.Normal, FontStretch.Normal);

        var graded = FontVariationList.Parse("GRAD=150").Apply(bold);
        var sized = FontVariationList.Parse("opsz=20").Apply(bold);

        Assert.Multiple(() =>
        {
            Assert.That(graded.Variations.Single(v => v.Tag == "wght").Value, Is.EqualTo(700));
            Assert.That(graded.Variations.Single(v => v.Tag == "GRAD").Value, Is.EqualTo(150));
            Assert.That(graded.AtOpticalSize(36).Variations.Single(v => v.Tag == "opsz").Value, Is.EqualTo(36));
            Assert.That(sized.AtOpticalSize(36), Is.SameAs(sized), "an optical size given by value stays");
        });
    }

    [Test]
    public void AnItalicOfAFontWithASlantAxis_IsSlanted()
    {
        var italic = RobotoFlex().GetFont(new FontWeight(400), FontStyle.Italic, FontStretch.Normal);

        Assert.That(italic.Variations.Single(v => v.Tag == "slnt").Value, Is.EqualTo(-10));
    }
}
