using System;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Animation;
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

    // On the way from weight 400 to 700 the text is laid out at its own weight and drawn between the key instances
    // around it; at either end it is the list itself, drawn as it is at rest.
    [Test]
    public void Between_LaysTheTextOutOnTheWay_AndDrawsItBetweenKeyInstances()
    {
        var regular = RobotoFlex().GetFont(new FontWeight(400), FontStyle.Normal, FontStretch.Normal);
        var light = FontVariationList.Parse("wght=400");
        var heavy = FontVariationList.Parse("wght=700");

        var moving = FontVariationList.Between(light, heavy, 125.0 / 300).Apply(regular);
        var fromTheFontsOwn = FontVariationList.Between(FontVariationList.Empty, heavy, 0.5).Apply(regular);

        Assert.Multiple(() =>
        {
            Assert.That(moving.Variations.Single(v => v.Tag == "wght").Value, Is.EqualTo(525).Within(1e-3));
            Assert.That(moving.Blend.From.Variations.Single(v => v.Tag == "wght").Value, Is.EqualTo(500));
            Assert.That(moving.Blend.To.Variations.Single(v => v.Tag == "wght").Value, Is.EqualTo(550));
            Assert.That(fromTheFontsOwn.Variations.Single(v => v.Tag == "wght").Value, Is.EqualTo(550).Within(1e-3),
                "an axis one list lacks moves from the font's value");
            Assert.That(FontVariationList.Between(light, heavy, 0), Is.SameAs(light));
            Assert.That(FontVariationList.Between(light, heavy, 1), Is.SameAs(heavy));
        });
    }

    // A change of FontVariations under a transition moves the text through the values between, and settles on the new
    // ones, drawn as at rest.
    [Test]
    public void ATransition_MovesTheText_AndSettlesOnTheNewValues()
    {
        var regular = RobotoFlex().GetFont(new FontWeight(400), FontStyle.Normal, FontStretch.Normal);
        var text = new TextBlock
        {
            FontFamily = RobotoFlex(),
            FontVariations = FontVariationList.Parse("wght=400"),
            Transitions =
            {
                new FontVariationListTransition
                {
                    Property = nameof(TextBlock.FontVariations), Duration = TimeSpan.FromSeconds(1), Easing = new LinearEasing()
                }
            }
        };
        new Rendering.TestRoot(100, 100).Add(text);

        text.FontVariations = FontVariationList.Parse("wght=700");
        AnimationManager.Tick(0.5);
        var halfway = text.FontVariations.Apply(regular);
        AnimationManager.Tick(0.6);

        Assert.Multiple(() =>
        {
            Assert.That(halfway.Variations.Single(v => v.Tag == "wght").Value, Is.EqualTo(550).Within(0.5));
            Assert.That(halfway.Blend, Is.Not.Null, "drawn between key instances while it moves");
            Assert.That(text.FontVariations, Is.EqualTo(FontVariationList.Parse("wght=700")));
            Assert.That(text.FontVariations.Apply(regular).Blend, Is.Null, "at rest, drawn as itself");
        });
    }

    [Test]
    public void AnItalicOfAFontWithASlantAxis_IsSlanted()
    {
        var italic = RobotoFlex().GetFont(new FontWeight(400), FontStyle.Italic, FontStretch.Normal);

        Assert.That(italic.Variations.Single(v => v.Tag == "slnt").Value, Is.EqualTo(-10));
    }
}
