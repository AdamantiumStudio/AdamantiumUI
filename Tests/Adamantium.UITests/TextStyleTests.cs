using System;
using System.Linq;
using System.Threading;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Extensions;
using NUnit.Framework;
using ParagraphStyle = Adamantium.UI.Controls.Text.ParagraphStyle;
using CharacterStyle = Adamantium.UI.Controls.Text.CharacterStyle;

namespace Adamantium.UITests;

// Named paragraph and character styles on a block's paragraphs and inlines: what an inline or a paragraph sets itself
// wins over its style, a style based on another sets over it, and changing a style lays the block out again.
[TestFixture]
public class TextStyleTests
{
    private static void Update(Window window)
    {
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }
    }

    private static (TextBlock Text, Window Window) Hosted(params Inline[] inlines)
    {
        var text = new TextBlock { FontSize = 16 };
        foreach (var inline in inlines)
        {
            text.Inlines.Add(inline);
        }

        var window = new Window { Width = 800, Height = 600, Content = new StackPanel { Children = { text } } };
        Update(window);
        return (text, window);
    }

    private static Paragraph Paragraph(string text, ParagraphStyle style = null)
    {
        var paragraph = new Paragraph { ParagraphStyle = style };
        paragraph.Inlines.Add(new Run { Text = text });
        return paragraph;
    }

    private static GlyphWordData First(TextBlock text, int position) =>
        text.Layout.GetTextData().First(glyph => glyph.PositionInString == position);

    [Test]
    public void AParagraphStyle_SetsItsParagraphAndItsText_UnderWhatTheParagraphSetsItself()
    {
        var heading = new ParagraphStyle
        {
            FontSize = 28, Foreground = new SolidColorBrush(Colors.Red), Margin = new Thickness(20, 0, 0, 0),
            TextIndent = 5,
        };
        var own = Paragraph("Own", heading);
        own.TextIndent = 0;
        var (text, _) = Hosted(Paragraph("Heading", heading), own, Paragraph("Body"));

        Assert.Multiple(() =>
        {
            Assert.That(First(text, 0).FontSize, Is.EqualTo(28));
            Assert.That(First(text, 0).Attributes.Foreground, Is.EqualTo(Colors.Red));
            Assert.That(First(text, 0).PenX, Is.EqualTo(25).Within(0.01), "the style's margin and indent");
            Assert.That(First(text, 8).PenX, Is.EqualTo(20).Within(0.01), "the paragraph's own indent wins");
            Assert.That(First(text, 12).FontSize, Is.EqualTo(16), "a paragraph without it keeps the block's");
        });
    }

    [Test]
    public void ACharacterStyle_SetsItsText_UnderWhatTheInlineSetsItself()
    {
        var emphasis = new CharacterStyle { Foreground = new SolidColorBrush(Colors.Blue), FontWeight = FontWeight.Bold };
        var plain = new Run { Text = "plain " };
        var styled = new Run { Text = "styled ", CharacterStyle = emphasis };
        var own = new Run { Text = "own", CharacterStyle = emphasis, Foreground = new SolidColorBrush(Colors.Green) };
        var (text, window) = Hosted(plain, styled, own);
        // A face not loaded yet is read on a worker; the text lays out again on the loop when it arrives.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (text.Layout.AttributedText.AttributesAt(6).Font?.Weight != FontWeight.Bold && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10);
            LoopSignal.Drain();
            WindowExtension.UpdateTree(window);
        }

        Assert.Multiple(() =>
        {
            Assert.That(First(text, 6).Attributes.Foreground, Is.EqualTo(Colors.Blue));
            Assert.That(text.Layout.AttributedText.AttributesAt(6).Font.Weight, Is.EqualTo(FontWeight.Bold),
                "the style's weight");
            Assert.That(text.Layout.AttributedText.AttributesAt(0).Font.Weight, Is.Not.EqualTo(FontWeight.Bold));
            Assert.That(First(text, 13).Attributes.Foreground, Is.EqualTo(Colors.Green), "the inline's own color wins");
            Assert.That(text.Layout.AttributedText.AttributesAt(13).Font.Weight, Is.EqualTo(FontWeight.Bold));
        });
    }

    [Test]
    public void AStyleBasedOnAnother_SetsOverIt()
    {
        var body = new CharacterStyle { Foreground = new SolidColorBrush(Colors.Blue), FontSize = 20 };
        var strong = new CharacterStyle { BasedOn = body, FontSize = 24 };
        var (text, _) = Hosted(new Run { Text = "strong", CharacterStyle = strong });

        Assert.Multiple(() =>
        {
            Assert.That(First(text, 0).FontSize, Is.EqualTo(24));
            Assert.That(First(text, 0).Attributes.Foreground, Is.EqualTo(Colors.Blue));
        });
    }

    [Test]
    public void AStylesBrush_IsDrawnIntoTheText_AsAnInlinesOwnIs()
    {
        var brush = new SolidColorBrush(Colors.Blue);
        var style = new CharacterStyle { Foreground = brush };
        Hosted(new Run { Text = "styled", CharacterStyle = style });

        Assert.That(brush.IsBakedIn, Is.True);
    }

    [Test]
    public void BasingAStyleOnOneBasedOnIt_Throws()
    {
        var a = new CharacterStyle();
        var b = new CharacterStyle { BasedOn = a };

        Assert.Throws<ArgumentException>(() => a.BasedOn = b);
        Assert.That(a.BasedOn, Is.Null);
    }

    [Test]
    public void AHyperlinksUnderline_YieldsToItsStyle_AnInlinesOwnDecorationDoesNot()
    {
        var struck = new CharacterStyle { TextDecorations = TextDecorations.Strikethrough };
        var link = new Hyperlink();
        link.Inlines.Add(new Run { Text = "styled", CharacterStyle = struck });
        var plain = new Hyperlink();
        plain.Inlines.Add(new Run { Text = "plain" });
        var (text, _) = Hosted(link, plain);

        Assert.Multiple(() =>
        {
            Assert.That(text.Layout.AttributedText.AttributesAt(0).Decorations, Is.EqualTo(TextDecorations.Strikethrough));
            Assert.That(text.Layout.AttributedText.AttributesAt(7).Decorations, Is.EqualTo(TextDecorations.Underline));
        });
    }

    [Test]
    public void AnObject_TakesItsStylesBaselineShift()
    {
        var raised = new CharacterStyle { BaselineShift = 5 };
        var container = new InlineUIContainer { Child = new Adamantium.UI.Controls.Decorators.Border { Width = 10, Height = 10 }, CharacterStyle = raised };
        var (text, _) = Hosted(new Run { Text = "a" }, container);

        Assert.That(text.Layout.AttributedText.AttributesAt(1).BaselineShift, Is.EqualTo(5));
    }

    [Test]
    public void ChangingAStyle_OrOneItIsBasedOn_LaysTheBlockOutAgain()
    {
        var body = new CharacterStyle { FontSize = 20 };
        var strong = new CharacterStyle { BasedOn = body };
        var heading = new ParagraphStyle { FontSize = 30 };
        var (text, window) = Hosted(Paragraph("Heading", heading), new Paragraph { Inlines = { new Run { Text = "x", CharacterStyle = strong } } });
        body.FontSize = 40;
        heading.Margin = new Thickness(10, 0, 0, 0);
        Update(window);

        Assert.Multiple(() =>
        {
            Assert.That(First(text, 8).FontSize, Is.EqualTo(40));
            Assert.That(First(text, 0).PenX, Is.EqualTo(10).Within(0.01));
        });
    }
}
