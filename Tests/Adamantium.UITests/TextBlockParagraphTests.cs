using System;
using System.Linq;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>Paragraphs in a TextBlock, as HTML's p: each starts on a line of its own, the inlines between them make
/// paragraphs of their own, and what a paragraph sets about its lines reaches the layout for its stretch only.</summary>
[TestFixture]
public class TextBlockParagraphTests
{
    private const char Separator = (char)0x2029;

    private const string Prose = "Once upon a time there lived a king whose daughters were all beautiful, but the " +
                                 "youngest was so beautiful that the sun itself was astonished whenever it shone.";

    private static Window Host(TextBlock text)
    {
        var window = new Window { Width = 600, Height = 400, Content = new Border { Child = text } };
        Update(window);
        return window;
    }

    private static void Update(Window window)
    {
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }
    }

    private static Paragraph Of(string text, Action<Paragraph> setUp = null)
    {
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(new Run { Text = text });
        setUp?.Invoke(paragraph);
        return paragraph;
    }

    private static double LineStart(TextBlock text, int line) => text.Layout.GetTextData()
        .Where(glyph => glyph.LineIndex == line && glyph.Symbol is not (' ' or '\n'))
        .Min(glyph => glyph.PenX);

    [Test]
    public void EachParagraphEndsInASeparator_TheInlinesBetweenMakeOneOfTheirOwn()
    {
        var text = new TextBlock();
        text.Inlines.Add(new Run { Text = "loose " });
        text.Inlines.Add(new Bold { Inlines = { new Run { Text = "bold" } } });
        text.Inlines.Add(Of("first"));
        text.Inlines.Add(Of("second"));
        text.Inlines.Add(new Run { Text = "tail" });
        Host(text);

        Assert.That(text.Layout.Text,
            Is.EqualTo("loose bold" + Separator + "first" + Separator + "second" + Separator + "tail"));
        Assert.That(text.Layout.LineCount, Is.EqualTo(4));
    }

    // As in HTML, inlines with no text between two paragraphs are no paragraph: no empty line stands for them.
    [Test]
    public void InlinesWithNoTextBetweenParagraphs_AddNoParagraph()
    {
        var text = new TextBlock();
        text.Inlines.Add(new Run { Text = "" });
        text.Inlines.Add(Of("first"));
        text.Inlines.Add(new Run { Text = "" });
        text.Inlines.Add(new Span());
        text.Inlines.Add(Of("second", paragraph => paragraph.Margin = new Thickness(0, 7, 0, 0)));
        text.Inlines.Add(new Run());
        Host(text);

        Assert.That(text.Layout.Text, Is.EqualTo("first" + Separator + "second"));
        var paragraphs = text.Layout.AttributedText.Paragraphs;
        Assert.That(paragraphs.Select(paragraph => (paragraph.Start, paragraph.Length)),
            Is.EqualTo(new[] { (0, 6), (6, 6) }), "each with the separator that ends it");
    }

    // As in HTML, white space between two paragraphs is no paragraph; without paragraphs it is the block's text.
    [Test]
    public void WhiteSpaceBetweenParagraphs_AddsNoParagraph()
    {
        var text = new TextBlock();
        text.Inlines.Add(Of("first"));
        text.Inlines.Add(new Run { Text = " \n ", FontSize = 40 });
        text.Inlines.Add(Of("second"));
        Host(text);

        Assert.That(text.Layout.Text, Is.EqualTo("first" + Separator + "second"));
        Assert.That(text.Layout.AttributedText.AttributesAt(6).FontSize, Is.Null, "the left-out run sets nothing");

        var plain = new TextBlock();
        plain.Inlines.Add(new Run { Text = " " });
        Host(plain);
        Assert.That(plain.Layout.Text, Is.EqualTo(" "));
    }

    [Test]
    public void AnEmptyLastParagraph_KeepsItsRoomAbove()
    {
        var text = new TextBlock();
        text.Inlines.Add(Of("first"));
        text.Inlines.Add(new Paragraph { Margin = new Thickness(0, 40, 0, 0) });
        Host(text);
        var plain = new TextBlock();
        plain.Inlines.Add(Of("first"));
        plain.Inlines.Add(new Paragraph());
        Host(plain);

        Assert.That(text.Layout.GetLine(1).Top - plain.Layout.GetLine(1).Top, Is.EqualTo(40).Within(1e-6));
    }

    [Test]
    public void ALineBreakEndingTheLastParagraph_AddsNoRoomBelowIt()
    {
        TextBlock Block(double below)
        {
            var text = new TextBlock();
            var paragraph = Of("last", set => set.Margin = new Thickness(0, 0, 0, below));
            paragraph.Inlines.Add(new LineBreak());
            text.Inlines.Add(paragraph);
            Host(text);
            return text;
        }

        Assert.That(Block(50).DesiredSize.Height, Is.EqualTo(Block(0).DesiredSize.Height).Within(1e-6));
    }

    [Test]
    public void AnEmptyParagraph_IsALineOfItsOwn()
    {
        var text = new TextBlock();
        text.Inlines.Add(Of("first"));
        text.Inlines.Add(new Paragraph());
        text.Inlines.Add(Of("third"));
        Host(text);

        Assert.That(text.Layout.Text, Is.EqualTo("first" + Separator + Separator + "third"));
        Assert.That(text.Layout.LineCount, Is.EqualTo(3));
    }

    [Test]
    public void WhatAParagraphSets_ReachesTheLayout_WhatItLeavesComesFromTheBlock()
    {
        var text = new TextBlock { HorizontalTextAlignment = HorizontalTextAlignment.Center };
        text.Inlines.Add(new Run { Text = "loose" });
        text.Inlines.Add(Of("set", paragraph =>
        {
            paragraph.HorizontalTextAlignment = HorizontalTextAlignment.Right;
            paragraph.Margin = new Thickness(10, 20, 30, 40);
            paragraph.TextIndent = -5;
            paragraph.LineHeight = 33;
        }));
        text.Inlines.Add(Of("unset"));
        Host(text);

        var paragraphs = text.Layout.AttributedText.Paragraphs;
        Assert.That(paragraphs, Has.Count.EqualTo(2), "the loose inlines stand as the block says");
        Assert.That(paragraphs[0].Attributes, Is.EqualTo(new ParagraphAttributes
        {
            Alignment = HorizontalTextAlignment.Right,
            MarginLeft = 10,
            SpaceBefore = 20,
            MarginRight = 30,
            SpaceAfter = 40,
            TextIndent = -5,
            LineHeight = 33,
        }));
        Assert.That(paragraphs[1].Attributes.Alignment, Is.Null);
        Assert.That(paragraphs[1].Attributes.LineHeight, Is.NaN);
    }

    [Test]
    public void AParagraphsMargin_MovesItsLinesOnly()
    {
        var text = new TextBlock { FontSize = 16 };
        text.Inlines.Add(Of("plain"));
        text.Inlines.Add(Of("moved", paragraph => paragraph.Margin = new Thickness(40, 0, 0, 0)));
        Host(text);

        Assert.That(LineStart(text, 1) - LineStart(text, 0), Is.EqualTo(40).Within(1));
    }

    [Test]
    public void InColumns_TheRoomAboveAParagraphHolds()
    {
        TextBlock Block(double above)
        {
            var text = new TextBlock { Columns = 2, Width = 400, Height = 300, TextWrapping = TextWrapping.WrapByWords };
            text.Inlines.Add(Of("one"));
            text.Inlines.Add(Of("two", paragraph => paragraph.Margin = new Thickness(0, above, 0, 0)));
            Host(text);
            return text;
        }

        Assert.That(Block(30).Layout.GetLine(1).Top - Block(0).Layout.GetLine(1).Top, Is.EqualTo(30).Within(1e-6));
    }

    [Test]
    public void AParagraphsFirstLineIndent_IndentsItsFirstLineOnly()
    {
        var text = new TextBlock { FontSize = 16, Width = 240, TextWrapping = TextWrapping.WrapByWords };
        text.Inlines.Add(Of(Prose, paragraph => paragraph.TextIndent = 30));
        Host(text);

        Assume.That(text.Layout.LineCount, Is.GreaterThan(2));
        Assert.That(LineStart(text, 0) - LineStart(text, 1), Is.EqualTo(30).Within(1));
        Assert.That(LineStart(text, 2), Is.EqualTo(LineStart(text, 1)).Within(1));
    }

    [Test]
    public void AParagraphsFormatting_GoesToItsRuns_AndToTheSeparatorThatEndsIt()
    {
        var text = new TextBlock { FontSize = 30 };
        text.Inlines.Add(Of("small", paragraph => paragraph.FontSize = 10));
        text.Inlines.Add(new Run { Text = "big" });
        Host(text);

        var attributed = text.Layout.AttributedText;
        Assert.That(attributed.AttributesAt(0).FontSize, Is.EqualTo(10));
        Assert.That(attributed.AttributesAt(5).FontSize, Is.EqualTo(10), "the separator is as small as its paragraph");
        Assert.That(attributed.AttributesAt(6).FontSize, Is.Null, "after it the block's own size");
    }

    [Test]
    public void AChangedParagraph_LaysTheBlockOutAgain()
    {
        var text = new TextBlock();
        var paragraph = Of("one");
        text.Inlines.Add(paragraph);
        text.Inlines.Add(Of("two"));
        var window = Host(text);

        paragraph.Margin = new Thickness(25, 0, 0, 0);
        Update(window);

        Assert.That(text.Layout.AttributedText.Paragraphs[0].Attributes.MarginLeft, Is.EqualTo(25));
    }

    [Test]
    public void AParagraphInsideASpan_IsRefused()
    {
        var span = new Span();

        Assert.Throws<InvalidOperationException>(() => span.Inlines.Add(new Paragraph()));
        Assert.Throws<InvalidOperationException>(() => new Paragraph().Inlines.Add(new Paragraph()));
        Assert.Throws<InvalidOperationException>(() => new Bold().Inlines.Add(new Paragraph()));
        Assert.That(span.Inlines, Is.Empty, "the refused paragraph is not kept");

        span.Inlines.Add(new Run());
        Assert.Throws<InvalidOperationException>(() => span.Inlines[0] = new Paragraph());
        Assert.That(span.Inlines[0], Is.InstanceOf<Run>());
    }

    // As CSS's text-transform: the block lays out and copies the case, its text and inlines keep their own.
    [Test]
    public void TextTransform_ShowsAndCopiesTheCase_TheTextKeepsItsOwn()
    {
        var plain = new TextBlock { Text = "plain text", TextTransform = TextTransform.Uppercase };
        var inlines = new TextBlock { TextTransform = TextTransform.Uppercase };
        inlines.Inlines.Add(Of("first"));
        inlines.Inlines.Add(new Bold { Inlines = { new Run { Text = "bold" } } });
        Host(plain);
        Host(inlines);
        plain.SelectAll();
        inlines.SelectAll();

        Assert.Multiple(() =>
        {
            Assert.That(plain.Layout.Text, Is.EqualTo("PLAIN TEXT"));
            Assert.That(plain.Text, Is.EqualTo("plain text"));
            Assert.That(plain.SelectedText, Is.EqualTo("PLAIN TEXT"));
            Assert.That(inlines.Layout.Text, Is.EqualTo("FIRST" + Separator + "BOLD"));
            Assert.That(inlines.SelectedText, Is.EqualTo("FIRST\nBOLD"));
        });
    }

    [Test]
    public void TextTransform_ChangedLater_LaysTheBlockOutAgain()
    {
        var text = new TextBlock { Text = "abc" };
        var window = Host(text);

        text.TextTransform = TextTransform.Uppercase;
        Update(window);

        Assert.That(text.Layout.Text, Is.EqualTo("ABC"));
    }

    [Test]
    public void TheEndOfAParagraph_IsCopiedAsANewLine()
    {
        var text = new TextBlock();
        text.Inlines.Add(Of("one"));
        text.Inlines.Add(Of("two"));
        Host(text);

        text.SelectAll();

        Assert.That(text.SelectedText, Is.EqualTo("one\ntwo"));
    }

    // The attributes, links and controls after a separator stand where their text went.
    [Test]
    public void ALinkAndAControlInALaterParagraph_StandWhereTheirTextWent()
    {
        var text = new TextBlock();
        text.Inlines.Add(Of("first"));
        var link = new Hyperlink { Inlines = { new Run { Text = "link" } } };
        var child = new Border { Width = 20, Height = 10 };
        var second = new Paragraph { Foreground = new SolidColorBrush(Colors.Red) };
        second.Inlines.Add(new Run { Text = "a " });
        second.Inlines.Add(link);
        second.Inlines.Add(new InlineUIContainer { Child = child });
        text.Inlines.Add(second);
        Host(text);

        Assert.That(text.Layout.Text, Is.EqualTo("first" + Separator + "a link" + (char)0xFFFC));
        Assert.That(text.LinkText(link), Is.EqualTo("link"));
        Assert.That(text.Layout.AttributedText.AttributesAt(6).Foreground, Is.EqualTo(Colors.Red));
        Assert.That(child.Bounds.X, Is.GreaterThan(LineStart(text, 1)), "the control stands in the second line");
        Assert.That(child.Bounds.Y, Is.GreaterThan(text.Layout.GetLine(0).Top + text.Layout.GetLine(0).Height - 1));
    }
}
