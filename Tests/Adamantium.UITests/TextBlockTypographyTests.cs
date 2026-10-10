using System;
using System.Linq;
using System.Threading;
using Adamantium.Fonts;
using Adamantium.Fonts.Shaping;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Localization;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Extensions;
using Adamantium.UITests.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests;

[TestFixture]
public class TextBlockTypographyTests
{
    private static TextBlock Hosted(TextBlock text, Border host = null)
    {
        Host(text, host);
        return text;
    }

    // A face not loaded yet is read on a worker, the family's own face standing in; the text lays out again on the loop
    // when it arrives. Runs the loop until it has, or ten seconds pass.
    private static TextBlock HostedUntil(TextBlock text, Func<bool> arrived, Border host = null)
    {
        var window = Host(text, host);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!arrived() && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10);
            LoopSignal.Drain();
            WindowExtension.UpdateTree(window);
        }

        return text;
    }

    private static Window Host(TextBlock text, Border host)
    {
        host ??= new Border();
        host.Child = text;
        var window = new Window { Width = 600, Height = 300, Content = host };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }

        return window;
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

    private sealed class Recorder : IDrawingContext
    {
        public RecordingDrawingSession Session { get; } = new();

        public IDrawingSession ForControl(IUIComponent component) => Session;
    }

    [Test]
    public void LinesThinnerThanAPixel_AreDrawnAWholePixelThick()
    {
        var text = new TextBlock { FontSize = 12 };
        text.Inlines.Add(new Run { Text = "struck out", TextDecorations = TextDecorations.Strikethrough });
        text.Inlines.Add(new Run { Text = "1", FontSize = 13, TextDecorations = TextDecorations.Underline });
        Hosted(text);
        var recorder = new Recorder();

        text.Render(recorder);

        var lines = text.Layout.GetAdornments();
        Assert.That(lines.Select(line => line.Rect.Height), Is.All.LessThan(1), "the font asks for hairlines");
        Assert.That(recorder.Session.Rectangles, Has.Count.EqualTo(lines.Count));
        foreach (var (_, drawn, _) in recorder.Session.Rectangles)
        {
            Assert.That(drawn.Height, Is.EqualTo(1).Within(1e-9));
            Assert.That(drawn.Y, Is.EqualTo(Math.Round(drawn.Y)).Within(1e-9), "on a pixel row");
        }
    }

    [Test]
    public void AWeightOnAContainer_PicksTheFamilysBoldFace()
    {
        var host = new Border { FontWeight = FontWeight.Bold };
        var text = new TextBlock { Text = "bold" };
        HostedUntil(text, () => text.Layout?.Font.FullName.Contains("Bold") == true, host);
        var plain = Hosted(new TextBlock { Text = "plain" });

        Assert.That(text.Layout.Font, Is.Not.SameAs(plain.Layout.Font), "another face of the family");
        Assert.That(text.Layout.Font.FullName, Does.Contain("Bold"));
    }

    [Test]
    public void SynthesisOnAContainer_DrawsWhatTheFaceLacks()
    {
        var host = new Border
        {
            FontWeight = FontWeight.Bold,
            FontStyle = FontStyle.Italic,
            FontSynthesis = FontSynthesis.Weight | FontSynthesis.Style
        };
        var text = Hosted(new TextBlock { Text = "synthesized" }, host);
        var regular = new TextBlock().ResolveFont(UIComponent.DefaultFontFamily);

        Assert.That(text.TextShaping(regular).Synthesis, Is.EqualTo(FontSynthesis.Weight | FontSynthesis.Style),
            "a regular face set for bold italic text is thickened and slanted");
        Assert.That(new TextBlock { FontWeight = FontWeight.Bold }.TextShaping(regular)?.Synthesis, Is.Null,
            "nothing is synthesized unless asked for");
    }

    [Test]
    public void ARunWithItsOwnWeight_IsSetInThatFace()
    {
        var text = new TextBlock();
        text.Inlines.Add(new Run { Text = "plain " });
        text.Inlines.Add(new Run { Text = "bold", FontWeight = FontWeight.Bold });
        HostedUntil(text, () => text.Layout?.GetTextData().Any(g => g.Font.FullName.Contains("Bold")) == true);

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
