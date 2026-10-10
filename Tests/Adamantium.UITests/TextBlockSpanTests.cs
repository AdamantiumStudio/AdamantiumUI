using System;
using System.Linq;
using System.Threading;
using Adamantium.Fonts;
using Adamantium.Fonts.Shaping;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>Spans inside a TextBlock: what a span sets goes to the inlines in it that do not set their own, spans nest,
/// their lines add up, Bold/Italic/Underline are spans with one thing set, and a LineBreak ends the line.</summary>
[TestFixture]
public class TextBlockSpanTests
{
    private static TextBlock Hosted(TextBlock text, Func<bool> arrived = null)
    {
        var window = new Window { Width = 600, Height = 300, Content = new Border { Child = text } };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }

        // A face not loaded yet is read on a worker, the family's own face standing in; the text lays out again on the
        // loop when it arrives.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (arrived != null && !arrived() && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10);
            LoopSignal.Drain();
            WindowExtension.UpdateTree(window);
        }

        return text;
    }

    private static TextAttributes At(TextBlock text, int index) => text.Layout.AttributedText.AttributesAt(index);

    [Test]
    public void ASpansFormatting_GoesToItsRuns_ARunsOwnWins()
    {
        var text = new TextBlock { FontSize = 14 };
        var span = new Span { FontSize = 30, Foreground = new SolidColorBrush(Colors.Red) };
        span.Inlines.Add(new Run { Text = "ab" });
        span.Inlines.Add(new Run { Text = "cd", FontSize = 10 });
        text.Inlines.Add(new Run { Text = "x" });
        text.Inlines.Add(span);
        Hosted(text);

        Assert.That(text.Layout.Text, Is.EqualTo("xabcd"));
        Assert.That(At(text, 0).FontSize, Is.Null, "outside the span: the block's own size");
        Assert.That(At(text, 1).FontSize, Is.EqualTo(30));
        Assert.That(At(text, 1).Foreground, Is.EqualTo(Colors.Red));
        Assert.That(At(text, 3).FontSize, Is.EqualTo(10), "the run's own size wins");
        Assert.That(At(text, 3).Foreground, Is.EqualTo(Colors.Red), "what the run leaves unset comes from the span");
    }

    [Test]
    public void DecorationLines_OfASpanAndItsRun_AddUp()
    {
        var text = new TextBlock();
        var span = new Span();
        span.DecorationLines.Add(new TextDecoration { Location = TextDecorationLocation.Overline, Thickness = 2 });
        var run = new Run { Text = "ab" };
        run.DecorationLines.Add(new TextDecoration
        {
            Brush = new SolidColorBrush(Colors.Red), Offset = 3, DashArray = [4, 2],
        });
        span.Inlines.Add(run);
        text.Inlines.Add(span);
        var window = new Window { Width = 600, Height = 300, Content = new Border { Child = text } };
        WindowExtension.UpdateTree(window);

        var lines = At(text, 0).DecorationLines;
        Assert.That(lines.Select(line => line.Location),
            Is.EqualTo(new[] { TextDecorationLocation.Overline, TextDecorationLocation.Underline }));
        Assert.That(lines[0].Thickness, Is.EqualTo(2));
        Assert.That(lines[1].Color, Is.EqualTo(Colors.Red));
        Assert.That(lines[1].Offset, Is.EqualTo(3));
        Assert.That(lines[1].Dashes, Is.EqualTo(new double[] { 4, 2 }));

        run.DecorationLines[0].Offset = 5;
        WindowExtension.UpdateTree(window);
        Assert.That(At(text, 0).DecorationLines[1].Offset, Is.EqualTo(5), "a changed line lays the text out again");

        run.DecorationLines[0].DashArray.Add(1);
        WindowExtension.UpdateTree(window);
        Assert.That(At(text, 0).DecorationLines[1].Dashes, Is.EqualTo(new double[] { 4, 2, 1 }), "so do its dashes");
        Assert.Throws<ArgumentNullException>(() => run.DecorationLines.Add(null));
    }

    [Test]
    public void BaselineAlignment_GoesToTheSpansRuns_ARunsOwnWins()
    {
        var text = new TextBlock();
        var span = new Span { BaselineAlignment = BaselineAlignment.Superscript };
        span.Inlines.Add(new Run { Text = "ab" });
        span.Inlines.Add(new Run { Text = "cd", BaselineAlignment = BaselineAlignment.Subscript });
        text.Inlines.Add(new Run { Text = "x" });
        text.Inlines.Add(span);
        Hosted(text);

        Assert.That(At(text, 0).BaselineAlignment, Is.Null);
        Assert.That(At(text, 1).BaselineAlignment, Is.EqualTo(BaselineAlignment.Superscript));
        Assert.That(At(text, 3).BaselineAlignment, Is.EqualTo(BaselineAlignment.Subscript));
    }

    [Test]
    public void SpansNest_TheNearerOneWins_LinesAddUp()
    {
        var text = new TextBlock();
        var outer = new Underline { FontSize = 20 };
        var inner = new Span { FontSize = 12 };
        inner.Inlines.Add(new Run { Text = "in", TextDecorations = TextDecorations.Strikethrough });
        outer.Inlines.Add(new Run { Text = "out" });
        outer.Inlines.Add(inner);
        text.Inlines.Add(outer);
        Hosted(text);

        Assert.That(At(text, 0).FontSize, Is.EqualTo(20));
        Assert.That(At(text, 0).Decorations, Is.EqualTo(TextDecorations.Underline));
        Assert.That(At(text, 3).FontSize, Is.EqualTo(12));
        Assert.That(At(text, 3).Decorations, Is.EqualTo(TextDecorations.Underline | TextDecorations.Strikethrough));
    }

    [Test]
    public void BoldAndItalic_PickTheFamilysBoldAndItalicFaces()
    {
        var text = new TextBlock();
        var bold = new Bold();
        var italic = new Italic();
        italic.Inlines.Add(new Run { Text = "bi" });
        bold.Inlines.Add(italic);
        text.Inlines.Add(new Run { Text = "r" });
        text.Inlines.Add(bold);
        Hosted(text, () => At(text, 1).Font.Style == FontStyle.Italic);

        Assert.That(At(text, 0).Font.Weight, Is.EqualTo(FontWeight.Normal));
        Assert.That(At(text, 1).Font.Weight, Is.EqualTo(FontWeight.Bold));
        Assert.That(At(text, 1).Font.Style, Is.EqualTo(FontStyle.Italic));
    }

    [Test]
    public void ALineBreak_EndsTheLine()
    {
        var text = new TextBlock();
        text.Inlines.Add(new Run { Text = "one" });
        text.Inlines.Add(new LineBreak());
        var bold = new Bold();
        bold.Inlines.Add(new Run { Text = "two" });
        text.Inlines.Add(bold);
        Hosted(text, () => At(text, 4).Font.Weight == FontWeight.Bold);

        Assert.That(text.Layout.Text, Is.EqualTo("one" + (char)0x2028 + "two"), "a line separator, not a new paragraph");
        Assert.That(text.Layout.LineCount, Is.EqualTo(2));
        Assert.That(At(text, 4).Font.Weight, Is.EqualTo(FontWeight.Bold), "the runs after it keep their own spans");
    }

    [Test]
    public void ARunDeepInSpans_ChangingItsText_LaysTheBlockOutAgain()
    {
        var text = new TextBlock();
        var run = new Run { Text = "short" };
        var inner = new Italic();
        inner.Inlines.Add(run);
        var outer = new Span();
        outer.Inlines.Add(inner);
        text.Inlines.Add(outer);
        Hosted(text);

        run.Text = "a much longer text";
        Hosted(text);

        Assert.That(text.Layout.Text, Is.EqualTo("a much longer text"));
    }

    [Test]
    public void TypographyOnASpan_SetAfterLayout_LaysTheTextOutAgain()
    {
        var text = new TextBlock();
        var span = new Span();
        span.Inlines.Add(new Run { Text = "caps" });
        text.Inlines.Add(span);
        Hosted(text);

        Typography.SetCapitals(span, FontCapitals.SmallCaps);
        Hosted(text);

        Assert.That(At(text, 0).Features, Has.Some.Matches<FontFeature>(feature => feature.ToString() == "smcp"));
    }

    [Test]
    public void ClearingASpan_LetsGoOfItsInlines()
    {
        var text = new TextBlock();
        var run = new Run { Text = "gone" };
        var span = new Span();
        span.Inlines.Add(run);
        span.Inlines.Add(new Run { Text = "too" });
        text.Inlines.Add(span);
        Hosted(text);

        span.Inlines.Clear();
        Hosted(text);
        var laidOut = text.Layout.Text;
        run.Text = "changed after it left";
        Hosted(text);

        Assert.That(run.LogicalParent, Is.Null);
        Assert.That(text.Layout.Text, Is.EqualTo(laidOut), "a run that left the span no longer reaches the block");
    }

    [Test]
    public void ASpanInsideItself_IsRefused()
    {
        var outer = new Span();
        var inner = new Bold();
        outer.Inlines.Add(inner);

        Assert.Throws<InvalidOperationException>(() => outer.Inlines.Add(outer));
        Assert.Throws<InvalidOperationException>(() => inner.Inlines.Add(outer));
    }

    [Test]
    public void BoldItalicAndUnderline_AreTheirDefaults_NotLocalValues()
    {
        var bold = new Bold();
        var italic = new Italic();
        var underline = new Underline();
        bold.ClearValue(Inline.FontWeightProperty);

        Assert.That(bold.FontWeight, Is.EqualTo(FontWeight.Bold), "clearing it goes back to bold, not to nothing");
        Assert.That(italic.FontStyle, Is.EqualTo(FontStyle.Italic));
        Assert.That(underline.TextDecorations, Is.EqualTo(TextDecorations.Underline));
    }

    [Test]
    public void ABlockOfInlines_IsNamedByItsText()
    {
        var text = new TextBlock();
        var bold = new Bold();
        bold.Inlines.Add(new Run { Text = "world" });
        text.Inlines.Add(new Run { Text = "hello " });
        text.Inlines.Add(bold);
        Hosted(text);

        Assert.That(text.GetAutomationPeer().Name, Is.EqualTo("hello world"));
    }

    [Test]
    public void ARunInASpan_InheritsTheBlocksDataContext()
    {
        var text = new TextBlock { DataContext = "context" };
        var run = new Run();
        var span = new Span();
        span.Inlines.Add(run);
        text.Inlines.Add(span);
        Hosted(text);

        Assert.That(run.DataContext, Is.EqualTo("context"));
    }
}
