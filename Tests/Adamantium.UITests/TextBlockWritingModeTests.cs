using System.Linq;
using Adamantium.Graphics.Fonts;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A TextBlock set vertically: its lines run down as far as it is high and stack from its right, it is as wide
/// as they take, and its columns stack one under another.</summary>
[TestFixture]
public class TextBlockWritingModeTests
{
    private const string Prose =
        "In olden times when wishing still helped one, there lived a king whose daughters were all beautiful, but " +
        "the youngest was so beautiful that the sun itself, which has seen so much, was astonished whenever it shone " +
        "in her face. Close by the king's castle lay a great dark forest.";

    private static TextBlock Hosted(TextBlock text, double height = double.NaN)
    {
        var window = new Window { Width = 900, Height = 600, Content = new Border { Height = height, Child = text } };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }

        return text;
    }

    [Test]
    public void LinesRunDownAsFarAsTheBlockIsHigh_AndStackFromItsRight()
    {
        var text = Hosted(new TextBlock
        {
            Text = Prose, TextWrapping = TextWrapping.WrapByWords, Height = 200,
            WritingMode = WritingMode.VerticalRightToLeft,
        });
        var glyphs = text.Layout.GetTextData();
        var lineHeight = text.Layout.GetLine(0).Height;

        Assert.That(text.Layout.LineCount, Is.GreaterThan(2));
        Assert.That(glyphs.All(glyph => glyph.Sideways), Is.True, "Latin lies turned");
        Assert.That(glyphs.Where(glyph => glyph.Symbol != ' ').Max(glyph => glyph.Rect.Bottom), Is.LessThanOrEqualTo(200.5),
            "a space may hang past the end of its line, as across");
        Assert.That(text.DesiredSize.Height, Is.EqualTo(200));
        Assert.That(text.DesiredSize.Width, Is.EqualTo(System.Math.Ceiling(text.Layout.LineCount * lineHeight)).Within(1));
        for (var line = 1; line < text.Layout.LineCount; line++)
        {
            var right = glyphs.Where(glyph => glyph.LineIndex == line).Max(glyph => glyph.Rect.Right);
            var left = glyphs.Where(glyph => glyph.LineIndex == line - 1).Min(glyph => glyph.Rect.Left);
            Assert.That(right, Is.LessThanOrEqualTo(left + 1), $"line {line} left of line {line - 1}");
        }
    }

    [Test]
    public void WithoutAHeight_LinesRunAsFarAsTheBlockIsGiven()
    {
        var text = Hosted(new TextBlock
        {
            Text = Prose, TextWrapping = TextWrapping.WrapByWords, WritingMode = WritingMode.VerticalRightToLeft,
        }, 300);

        Assert.That(text.Layout.LineCount, Is.GreaterThan(1));
        Assert.That(text.Layout.GetTextData().Where(glyph => glyph.Symbol != ' ').Max(glyph => glyph.Rect.Bottom),
            Is.LessThanOrEqualTo(300.5));
    }

    [Test]
    public void TrimmedWithoutAHeight_EndsInAnEllipsisAtTheBottomOfItsSlot()
    {
        var text = Hosted(new TextBlock
        {
            Text = Prose, TextTrimming = TextTrimming.CharEllipses, WritingMode = WritingMode.VerticalRightToLeft,
        }, 300);
        var glyphs = text.Layout.GetTextData();

        Assert.That(glyphs.Count(glyph => glyph.Symbol == '.' && glyph.PositionInString < 0), Is.EqualTo(3));
        Assert.That(glyphs.Max(glyph => glyph.Rect.Bottom), Is.LessThanOrEqualTo(300.5));
        Assert.That(text.DesiredSize.Height, Is.LessThanOrEqualTo(300));
    }

    [Test]
    public void InAWiderSlot_TheFirstLineStandsAtItsRightEdge()
    {
        var text = Hosted(new TextBlock
        {
            Text = "Once upon a time", TextWrapping = TextWrapping.WrapByWords, Height = 200,
            WritingMode = WritingMode.VerticalRightToLeft,
        });
        var lines = text.Layout.CalculatedLayoutSize.Width;

        Assert.That(text.RenderSize.Width, Is.GreaterThan(lines + 100), "stretched across the window");
        Assert.That(text.LinesShift(), Is.EqualTo(System.Math.Round(text.RenderSize.Width - lines)));
        Assert.That(text.Layout.GetTextData().Max(glyph => glyph.Rect.Right) + text.LinesShift(),
            Is.EqualTo(text.RenderSize.Width).Within(text.Layout.GetLine(0).Height / 2), "the first line at the right");
    }

    [Test]
    public void Columns_StackOneUnderAnother()
    {
        var text = Hosted(new TextBlock
        {
            Text = Prose, TextWrapping = TextWrapping.WrapByWords, Height = 400, Columns = 2, ColumnGap = 20,
            WritingMode = WritingMode.VerticalRightToLeft,
        });
        var frames = text.Layout.Frames;

        Assert.That(frames, Has.Count.EqualTo(2));
        Assert.That(frames[1].Top, Is.EqualTo(frames[0].Bottom + 20).Within(0.01));
        Assert.That(frames[0].Height + frames[1].Height + 20, Is.EqualTo(400).Within(0.01));
        Assert.That(text.IsOverset, Is.False);
        Assert.That(text.Layout.GetTextData().All(glyph => frames.Any(frame => frame.Contains(glyph.Rect.Center))),
            Is.True, "every glyph inside a column");
    }
}
