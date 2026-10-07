using System.Linq;
using Adamantium.Fonts;
using Adamantium.Fonts.Shaping;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core.Localization;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

[TestFixture]
public class TextBlockTypographyTests
{
    private static TextBlock Hosted(TextBlock text, Border host = null)
    {
        host ??= new Border();
        host.Child = text;
        var window = new Window { Width = 600, Height = 300, Content = host };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }

        return text;
    }

    private static string[] Features(TextBlock text) =>
        text.Layout.AttributedText.Defaults.Features.Select(f => f.ToString()).ToArray();

    [Test]
    public void FontFeaturesOnAContainer_ReachTheText()
    {
        var host = new Border { FontFeatures = FontFeatureList.Parse("liga=0, ss01") };
        var text = Hosted(new TextBlock { Text = "office" }, host);

        string[] expected = ["liga=0", "ss01"];
        Assert.That(Features(text), Is.EqualTo(expected));
    }

    [Test]
    public void TypographyChoices_ComeBeforeTheElementsOwnFeatures()
    {
        var text = new TextBlock { Text = "1/2 Small", FontFeatures = [FontFeature.Kerning.Off] };
        Typography.SetCapitals(text, FontCapitals.SmallCaps);
        Typography.SetNumeralAlignment(text, FontNumeralAlignment.Tabular);
        Hosted(text);

        string[] expected = ["smcp", "tnum", "kern=0"];
        Assert.That(Features(text), Is.EqualTo(expected));
    }

    [Test]
    public void TextWithoutALanguage_IsInTheApplicationsLanguage()
    {
        var previous = Languages.Current;
        try
        {
            Languages.Current = "sr";
            var text = Hosted(new TextBlock { Text = "бгдп" });
            var own = Hosted(new TextBlock { Text = "бгдп", Language = "bg" });

            Assert.That(text.Layout.AttributedText.Defaults.Language, Is.EqualTo("sr"));
            Assert.That(own.Layout.AttributedText.Defaults.Language, Is.EqualTo("bg"));
        }
        finally
        {
            Languages.Current = previous;
        }
    }

    [Test]
    public void PlainText_StaysOnThePlainPath()
    {
        var previous = Languages.Current;
        try
        {
            Languages.Current = null;
            var text = Hosted(new TextBlock { Text = "plain" });

            Assert.That(text.Layout.AttributedText, Is.Null);
        }
        finally
        {
            Languages.Current = previous;
        }
    }

    [Test]
    public void Runs_LayOutAsOneText_WithTheirOwnSizesBackgroundsAndLines()
    {
        var text = new TextBlock { FontSize = 14 };
        text.Inlines.Add(new Run { Text = "small " });
        text.Inlines.Add(new Run
        {
            Text = "LARGE",
            FontSize = 28,
            Background = new SolidColorBrush(Colors.Red),
            TextDecorations = TextDecorations.Underline,
        });
        Hosted(text);

        var layout = text.Layout;
        var adornments = layout.GetAdornments();
        var large = new TextBlock { Text = "LARGE", FontSize = 28 };
        Hosted(large);

        Assert.That(layout.Text, Is.EqualTo("small LARGE"));
        Assert.That(layout.LineCount, Is.EqualTo(1));
        Assert.That(layout.GetLine(0).Height, Is.EqualTo(large.Layout.GetLine(0).Height).Within(1e-4),
            "the line is as tall as its largest run");
        Assert.That(adornments.Single(a => a.Kind == TextAdornmentKind.Background).Color, Is.EqualTo(Colors.Red));
        Assert.That(adornments.Single(a => a.Kind == TextAdornmentKind.Underline).Rect.X,
            Is.EqualTo(layout.GetCaretStops()[6].X).Within(1e-4), "the line starts under the second run");
    }

    [Test]
    public void AWeightOnAContainer_PicksTheFamilysBoldFace()
    {
        var host = new Border { FontWeight = FontWeight.Bold };
        var text = Hosted(new TextBlock { Text = "bold" }, host);
        var plain = Hosted(new TextBlock { Text = "plain" });

        Assert.That(text.Layout.Font, Is.Not.SameAs(plain.Layout.Font), "another face of the family");
        Assert.That(text.Layout.Font.FullName, Does.Contain("Bold"));
    }

    [Test]
    public void ARunWithItsOwnWeight_IsSetInThatFace()
    {
        var text = new TextBlock();
        text.Inlines.Add(new Run { Text = "plain " });
        text.Inlines.Add(new Run { Text = "bold", FontWeight = FontWeight.Bold });
        Hosted(text);

        var glyphs = text.Layout.GetTextData();

        Assert.That(glyphs.Where(g => g.PositionInString < 5).All(g => g.Font == text.Layout.Font));
        Assert.That(glyphs.Where(g => g.PositionInString >= 6).All(g => g.Font.FullName.Contains("Bold")));
    }

    [Test]
    public void ChangingARun_ReshapesTheBlock()
    {
        var run = new Run { Text = "one" };
        var text = new TextBlock();
        text.Inlines.Add(run);
        Hosted(text);

        run.Text = "one two";
        Hosted(text);

        Assert.That(text.Layout.Text, Is.EqualTo("one two"));
    }
}
