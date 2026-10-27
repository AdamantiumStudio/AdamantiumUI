using System;
using System.Linq;
using System.Threading;
using Adamantium.Core.Fonts;
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
    public void ClearingOverrides_LeavesASpanAndTheInlinesInsideItOnlyTheirStyle()
    {
        var emphasis = new CharacterStyle { FontSize = 24, Foreground = new SolidColorBrush(Colors.Red) };
        var inner = new Run { Text = "b", FontSize = 40, Tracking = 200 };
        var span = new Span { CharacterStyle = emphasis, Foreground = new SolidColorBrush(Colors.Blue) };
        span.Inlines.Add(new Run { Text = "a", FontWeight = FontWeight.Bold });
        span.Inlines.Add(inner);
        var (text, window) = Hosted(span);
        Assume.That(First(text, 1).FontSize, Is.EqualTo(40));

        span.ClearOverrides();
        Update(window);

        Assert.Multiple(() =>
        {
            Assert.That(First(text, 0).FontSize, Is.EqualTo(24), "the style's size");
            Assert.That(First(text, 1).FontSize, Is.EqualTo(24), "inside too");
            Assert.That(First(text, 0).Attributes.Foreground, Is.EqualTo(Colors.Red), "the style's color");
            Assert.That(span.Inlines.First().FontWeight, Is.Null, "no weight of its own");
            Assert.That(inner.Tracking, Is.Null);
            Assert.That(span.CharacterStyle, Is.SameAs(emphasis), "the style stays");
        });
    }

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

    private static Hyperlink Link(string text, CharacterStyle style = null)
    {
        var link = new Hyperlink { CharacterStyle = style };
        link.Inlines.Add(new Run { Text = text });
        // As the theme colors it.
        link.SetValue(Inline.ForegroundProperty, new SolidColorBrush(Colors.Blue), ValuePriority.Style);
        return link;
    }

    [Test]
    public void AHyperlinksLook_YieldsToAStyleOnIt_NotToOneAroundIt()
    {
        var plain = new CharacterStyle { Foreground = new SolidColorBrush(Colors.Red), TextDecorations = TextDecorations.None };
        var around = new Span { CharacterStyle = plain };
        around.Inlines.Add(Link("inner"));
        var own = Link("own", plain);
        own.Foreground = new SolidColorBrush(Colors.Green);
        var (text, _) = Hosted(Link("styled", plain), around, own);

        Assert.Multiple(() =>
        {
            Assert.That(text.Layout.AttributedText.AttributesAt(0).Decorations, Is.Null.Or.EqualTo(TextDecorations.None));
            Assert.That(First(text, 0).Attributes.Foreground, Is.EqualTo(Colors.Red), "the style on the link");
            Assert.That(text.Layout.AttributedText.AttributesAt(6).Decorations, Is.EqualTo(TextDecorations.Underline));
            Assert.That(First(text, 6).Attributes.Foreground, Is.EqualTo(Colors.Blue), "a style around the link");
            Assert.That(First(text, 11).Attributes.Foreground, Is.EqualTo(Colors.Green), "the link's own color");
        });
    }

    [Test]
    public void AParagraphStyle_AddsItsLinesToALinksUnderline_ItsNoneTakesNothingAway()
    {
        var clean = new ParagraphStyle { TextDecorations = TextDecorations.None };
        var struck = new ParagraphStyle { TextDecorations = TextDecorations.Strikethrough };
        var (text, _) = Hosted(new Paragraph { ParagraphStyle = clean, Inlines = { Link("a") } },
            new Paragraph { ParagraphStyle = struck, Inlines = { Link("b"), new Run { Text = "c" } } });

        Assert.Multiple(() =>
        {
            Assert.That(text.Layout.AttributedText.AttributesAt(0).Decorations, Is.EqualTo(TextDecorations.Underline));
            Assert.That(text.Layout.AttributedText.AttributesAt(2).Decorations,
                Is.EqualTo(TextDecorations.Underline | TextDecorations.Strikethrough));
            Assert.That(text.Layout.AttributedText.AttributesAt(3).Decorations, Is.EqualTo(TextDecorations.Strikethrough));
        });
    }

    [Test]
    public void AnUnderlinesLine_AndTheLinesInsideALink_AddUp()
    {
        var struck = new CharacterStyle { TextDecorations = TextDecorations.Strikethrough };
        var over = new CharacterStyle { TextDecorations = TextDecorations.Overline };
        var underlined = new Underline();
        underlined.Inlines.Add(new Run { Text = "a", CharacterStyle = struck });
        var aroundLink = new Underline();
        aroundLink.Inlines.Add(Link("b", struck));
        var link = new Hyperlink { CharacterStyle = over };
        link.Inlines.Add(new Run { Text = "c", TextDecorations = TextDecorations.Strikethrough });
        var overridden = new Hyperlink();
        overridden.Inlines.Add(new Run { Text = "d", CharacterStyle = struck, TextDecorations = TextDecorations.Overline });
        var both = new Paragraph
        {
            ParagraphStyle = new ParagraphStyle { TextDecorations = TextDecorations.Strikethrough }, CharacterStyle = over,
            Inlines = { new Run { Text = "e" } },
        };
        var (text, _) = Hosted(underlined, aroundLink, link, overridden, both);

        Assert.Multiple(() =>
        {
            Assert.That(text.Layout.AttributedText.AttributesAt(0).Decorations,
                Is.EqualTo(TextDecorations.Underline | TextDecorations.Strikethrough), "a style inside an underline");
            Assert.That(text.Layout.AttributedText.AttributesAt(1).Decorations,
                Is.EqualTo(TextDecorations.Underline | TextDecorations.Strikethrough), "an underline around a link");
            Assert.That(text.Layout.AttributedText.AttributesAt(2).Decorations,
                Is.EqualTo(TextDecorations.Overline | TextDecorations.Strikethrough), "a run's own line in a styled link");
            Assert.That(text.Layout.AttributedText.AttributesAt(3).Decorations,
                Is.EqualTo(TextDecorations.Underline | TextDecorations.Overline), "a style its run's own line overrides");
            Assert.That(text.Layout.AttributedText.AttributesAt(5).Decorations,
                Is.EqualTo(TextDecorations.Overline | TextDecorations.Strikethrough), "a paragraph's two styles");
        });
    }

    [Test]
    public void ABoldsWeight_AndAnItalicsSlant_YieldToAStyleOnThemOrInside_NotToOneAround()
    {
        var family = Adamantium.UI.Controls.Base.UIComponent.DefaultFontFamily;
        Assume.That(family.GetFont(FontWeight.Light, FontStyle.Normal, FontStretch.Normal).Weight, Is.EqualTo(FontWeight.Light),
            "the family has a Light face");
        Assume.That(family.GetFont(FontWeight.Black, FontStyle.Normal, FontStretch.Normal).Weight, Is.EqualTo(FontWeight.Black),
            "the family has a Black face");

        var light = new CharacterStyle { FontWeight = FontWeight.Light, FontStyle = FontStyle.Normal };
        var inside = new Bold();
        inside.Inlines.Add(new Run { Text = "a", CharacterStyle = light });
        var on = new Italic { CharacterStyle = light };
        on.Inlines.Add(new Run { Text = "b" });
        var around = new Span { CharacterStyle = light };
        var bold = new Bold();
        bold.Inlines.Add(new Italic { Inlines = { new Run { Text = "c" } } });
        around.Inlines.Add(bold);
        var own = new Bold();
        own.Inlines.Add(new Run { Text = "d", CharacterStyle = light, FontWeight = FontWeight.Black });
        var deeper = new Bold { CharacterStyle = light };
        deeper.Inlines.Add(new Run { Text = "e", CharacterStyle = new CharacterStyle { FontSize = 20 } });
        var link = Link("f", new CharacterStyle { Foreground = new SolidColorBrush(Colors.Red) });
        ((Run)link.Inlines[0]).CharacterStyle = new CharacterStyle { FontSize = 20 };
        var wrapped = new Bold();
        wrapped.Inlines.Add(new Span
        {
            CharacterStyle = light,
            Inlines = { new Run { Text = "g", CharacterStyle = new CharacterStyle { FontSize = 20 } } },
        });
        var (text, window) = Hosted(inside, on, around, own, deeper, link, wrapped);
        // Faces not loaded yet are read on a worker; the text lays out again on the loop when they arrive.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while ((text.Layout.AttributedText.AttributesAt(0).Font?.Weight != FontWeight.Light
                || text.Layout.AttributedText.AttributesAt(2).Font?.Weight != FontWeight.Bold
                || text.Layout.AttributedText.AttributesAt(3).Font?.Weight != FontWeight.Black
                || text.Layout.AttributedText.AttributesAt(4).Font?.Weight != FontWeight.Bold
                || text.Layout.AttributedText.AttributesAt(6).Font?.Weight != FontWeight.Bold)
               && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10);
            LoopSignal.Drain();
            WindowExtension.UpdateTree(window);
        }

        Assert.Multiple(() =>
        {
            Assert.That(text.Layout.AttributedText.AttributesAt(0).Font.Weight, Is.EqualTo(FontWeight.Light),
                "a style inside a bold");
            Assert.That(text.Layout.AttributedText.AttributesAt(1).Font.Style, Is.EqualTo(FontStyle.Normal),
                "a style on an italic");
            Assert.That(text.Layout.AttributedText.AttributesAt(2).Font.Weight, Is.EqualTo(FontWeight.Bold),
                "a style around a bold");
            Assert.That(text.Layout.AttributedText.AttributesAt(2).Font.Style, Is.EqualTo(FontStyle.Italic),
                "a style around an italic");
            Assert.That(text.Layout.AttributedText.AttributesAt(3).Font.Weight, Is.EqualTo(FontWeight.Black),
                "a weight set on the run");
            Assert.That(text.Layout.AttributedText.AttributesAt(4).Font.Weight, Is.EqualTo(FontWeight.Bold),
                "a style on the bold, replaced whole by one without a weight inside");
            Assert.That(First(text, 5).Attributes.Foreground, Is.EqualTo(Colors.Blue),
                "a style on a link, replaced whole by one without a color inside");
            Assert.That(text.Layout.AttributedText.AttributesAt(6).Font.Weight, Is.EqualTo(FontWeight.Bold),
                "a bold around a style that does not reach its run");
        });
    }

    [Test]
    public void ALoopClosedByMarkup_AndOpenedAgain_StillTellsTheText()
    {
        var a = new CharacterStyle { FontSize = 20 };
        var b = new CharacterStyle { BasedOn = a };
        b.SetValue(CharacterStyle.BasedOnProperty, a);
        a.SetValue(CharacterStyle.BasedOnProperty, b);
        var (text, window) = Hosted(new Run { Text = "x", CharacterStyle = b });
        a.ClearValue(CharacterStyle.BasedOnProperty);
        Update(window);
        a.FontSize = 30;
        Update(window);

        Assert.That(First(text, 0).FontSize, Is.EqualTo(30));
    }

    [Test]
    public void ALongLoopClosedByMarkup_LaysOut()
    {
        var styles = Enumerable.Range(0, 70).Select(_ => new CharacterStyle()).ToArray();
        for (var i = 1; i < styles.Length; i++)
        {
            styles[i].BasedOn = styles[i - 1];
        }

        styles[0].SetValue(CharacterStyle.BasedOnProperty, styles[^1]);
        styles[35].FontSize = 21;
        var (text, _) = Hosted(new Run { Text = "x", CharacterStyle = styles[^1] });

        Assert.That(First(text, 0).FontSize, Is.EqualTo(16), "a style in a loop stands alone");
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

    // A block's own styles style its Text with no inlines, as they would a paragraph's runs.
    [Test]
    public void TheBlocksStyles_StyleItsPlainText()
    {
        UntilTheBoldFaceHasLoaded();
        var text = new TextBlock
        {
            Text = "Plain",
            CharacterStyle = new CharacterStyle { FontSize = 30, FontWeight = FontWeight.Bold },
            ParagraphStyle = new ParagraphStyle { TextIndent = 12 },
        };
        var window = new Window { Width = 800, Height = 600, Content = new StackPanel { Children = { text } } };
        Update(window);

        Assert.Multiple(() =>
        {
            Assert.That(First(text, 0).FontSize, Is.EqualTo(30));
            Assert.That(First(text, 0).Font.Weight.Value, Is.EqualTo(FontWeight.Bold.Value), "the bold face");
            Assert.That(First(text, 0).PenX, Is.EqualTo(12).Within(0.01), "the paragraph style's indent");
        });
    }

    // What the block sets itself wins over its styles; an inline's own style wins over the block's.
    [Test]
    public void WhatTheBlockSetsItself_AndAnInlinesStyle_WinOverTheBlocksStyle()
    {
        var text = new TextBlock { FontSize = 16, CharacterStyle = new CharacterStyle { FontSize = 30 } };
        text.Inlines.Add(new Run { Text = "a" });
        text.Inlines.Add(new Run { Text = "b", CharacterStyle = new CharacterStyle { FontSize = 40 } });
        var window = new Window { Width = 800, Height = 600, Content = new StackPanel { Children = { text } } };
        Update(window);

        Assert.Multiple(() =>
        {
            Assert.That(First(text, 0).FontSize, Is.EqualTo(16), "the block's own size");
            Assert.That(First(text, 1).FontSize, Is.EqualTo(40), "the inline's style");
        });
    }

    // The block's styles reach its inlines as they reach its plain text; a paragraph's own style is nearer than them.
    [Test]
    public void TheBlocksStyles_StyleItsInlines_UnderAParagraphsOwnStyle()
    {
        UntilTheBoldFaceHasLoaded();
        var text = new TextBlock { CharacterStyle = new CharacterStyle { FontSize = 12, FontWeight = FontWeight.Bold } };
        text.Inlines.Add(new Run { Text = "body" });
        var heading = new Paragraph { ParagraphStyle = new ParagraphStyle { FontSize = 28 } };
        heading.Inlines.Add(new Run { Text = "Title" });
        text.Inlines.Add(heading);
        var window = new Window { Width = 800, Height = 600, Content = new StackPanel { Children = { text } } };
        Update(window);
        var title = text.Layout.GetTextData().First(glyph => glyph.Symbol == 'T');

        Assert.Multiple(() =>
        {
            Assert.That(First(text, 0).FontSize, Is.EqualTo(12));
            Assert.That(First(text, 0).Font.Weight.Value, Is.EqualTo(FontWeight.Bold.Value), "the block style's face");
            Assert.That(title.FontSize, Is.EqualTo(28), "the paragraph's own style");
        });
    }

    // The bold face loads in the background the first time it is asked for: laid out until it has arrived.
    private static void UntilTheBoldFaceHasLoaded()
    {
        var bold = new TextBlock { Text = "x", FontWeight = FontWeight.Bold };
        var window = new Window { Width = 200, Height = 100, Content = bold };
        for (var i = 0; i < 300 && bold.Layout?.GetTextData().FirstOrDefault()?.Font.Weight.Value != FontWeight.Bold.Value; i++)
        {
            Update(window);
            LoopSignal.Drain();
            System.Threading.Thread.Sleep(10);
        }
    }

    // A run's own style takes the place of the block's entirely, its face too; a run in a plain paragraph or span takes
    // the block's.
    [Test]
    public void ARunsOwnStyle_TakesTheBlocksFaceAway_APlainContainerKeepsIt()
    {
        UntilTheBoldFaceHasLoaded();
        var text = new TextBlock { CharacterStyle = new CharacterStyle { FontWeight = FontWeight.Bold, FontSize = 31 } };
        text.Inlines.Add(new Run { Text = "a" });
        text.Inlines.Add(new Run { Text = "b", CharacterStyle = new CharacterStyle { FontSize = 30 } });
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(new Run { Text = "c" });
        text.Inlines.Add(paragraph);
        var span = new Span();
        span.Inlines.Add(new Run { Text = "d" });
        text.Inlines.Add(span);
        var window = new Window { Width = 800, Height = 600, Content = new StackPanel { Children = { text } } };
        Update(window);
        GlyphWordData Letter(char symbol) => text.Layout.GetTextData().First(glyph => glyph.Symbol == symbol);

        Assert.Multiple(() =>
        {
            Assert.That(Letter('a').Font.Weight.Value, Is.EqualTo(FontWeight.Bold.Value), "the block style's face");
            Assert.That(Letter('b').Font.Weight.Value, Is.EqualTo(FontWeight.Normal.Value), "its own style, not the block's");
            Assert.That(Letter('b').FontSize, Is.EqualTo(30));
            Assert.That(Letter('c').Font.Weight.Value, Is.EqualTo(FontWeight.Bold.Value), "in a plain paragraph");
            Assert.That(Letter('c').FontSize, Is.EqualTo(31));
            Assert.That(Letter('d').Font.Weight.Value, Is.EqualTo(FontWeight.Bold.Value), "in a plain span");
        });
    }

    [Test]
    public void ChangingTheBlocksStyle_LaysItOutAgain()
    {
        var style = new CharacterStyle { FontSize = 20 };
        var text = new TextBlock { Text = "Plain", CharacterStyle = style };
        var window = new Window { Width = 800, Height = 600, Content = new StackPanel { Children = { text } } };
        Update(window);

        style.FontSize = 36;
        Update(window);

        Assert.That(First(text, 0).FontSize, Is.EqualTo(36));
    }

    [Test]
    public void ATextBoxsStyles_StyleItsText()
    {
        UntilTheBoldFaceHasLoaded();
        var style = new CharacterStyle { FontSize = 30, FontWeight = FontWeight.Bold };
        var box = new TextBox { Text = "Edit me", CharacterStyle = style };
        var empty = new TextBox { CharacterStyle = style };
        box.MeasureSurface(300);
        var glyph = box.Layout.GetTextData().First(g => g.PositionInString == 0);

        Assert.Multiple(() =>
        {
            Assert.That(glyph.FontSize, Is.EqualTo(30));
            Assert.That(glyph.Font.Weight.Value, Is.EqualTo(FontWeight.Bold.Value));
            Assert.That(empty.MeasureSurface(300).Height, Is.EqualTo(box.MeasureSurface(300).Height).Within(1e-3),
                "an empty field's line is as high as its text's");
        });

        style.FontSize = 18;
        box.MeasureSurface(300);
        Assert.That(box.Layout.GetTextData().First(g => g.PositionInString == 0).FontSize, Is.EqualTo(18),
            "a changed style lays the box out again");
    }
}
