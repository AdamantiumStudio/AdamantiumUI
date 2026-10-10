using Adamantium.Graphics.Fonts;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>Line height, line stacking and line spacing on text, inherited as WPF's, and a text block's most lines, as
/// Avalonia's.</summary>
[TestFixture]
public class LineHeightTests
{
    private const string Prose = "Once upon a time there lived a king whose daughters were all beautiful, but the " +
                                 "youngest was so beautiful that the sun itself was astonished whenever it shone.";

    private static Window Host(UIComponent content)
    {
        var window = new Window { Width = 600, Height = 400, Content = content };
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

    [Test]
    public void ALineHeightSetAbove_SpacesTheBlocksLines()
    {
        var text = new TextBlock { Text = "one\ntwo\nthree", FontSize = 16 };
        var border = new Border { Child = text, LineHeight = 30 };
        Host(border);

        Assert.That(text.Layout.GetLine(1).Top - text.Layout.GetLine(0).Top, Is.EqualTo(30).Within(1e-6));
        Assert.That(text.DesiredSize.Height, Is.EqualTo(90).Within(1));
    }

    [Test]
    public void LineSpacing_And_LineStacking_ReachTheLayout()
    {
        var text = new TextBlock
        {
            Text = "one\ntwo",
            LineHeight = 24,
            LineSpacing = 6,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        };
        var window = Host(new Border { Child = text });

        Assert.That(text.Layout.GetLine(1).Top - text.Layout.GetLine(0).Top, Is.EqualTo(30).Within(1e-6));

        text.LineSpacing = 0;
        Update(window);
        Assert.That(text.Layout.GetLine(1).Top - text.Layout.GetLine(0).Top, Is.EqualTo(24).Within(1e-6),
            "a change lays the text out again");
    }

    [Test]
    public void MaxLines_ShowsThatManyLines_AndTheBlockIsAsHigh()
    {
        var text = new TextBlock { Text = Prose, Width = 200, TextWrapping = TextWrapping.WrapByWords };
        var window = Host(new Border { Child = text });
        var all = text.Layout.LineCount;

        text.MaxLines = 2;
        Update(window);

        Assert.That(all, Is.GreaterThan(2));
        Assert.That(text.Layout.LineCount, Is.EqualTo(2));
        Assert.That(text.IsOverset, Is.True);
        Assert.That(text.DesiredSize.Height,
            Is.EqualTo(text.Layout.GetLine(1).Top + text.Layout.GetLine(1).Height - text.Layout.GetLine(0).Top)
                .Within(1));
    }

    [Test]
    public void MaxLines_Trimmed_EndsInAnEllipsis()
    {
        var text = new TextBlock
        {
            Text = Prose, Width = 200, TextWrapping = TextWrapping.WrapByWords,
            TextTrimming = TextTrimming.CharEllipses, MaxLines = 2,
        };
        Host(new Border { Child = text });

        Assert.That(text.Layout.LineCount, Is.EqualTo(2));
        Assert.That(System.Linq.Enumerable.Count(text.Layout.GetTextData(),
            glyph => glyph.PositionInString < 0 && glyph.Symbol == '.'), Is.EqualTo(3));
    }

    [Test]
    public void MaxLines_InColumns_DoesNotGrowThemForever()
    {
        var text = new TextBlock
        {
            Text = Prose + " " + Prose, Width = 400, TextWrapping = TextWrapping.WrapByWords, Columns = 2, MaxLines = 3,
        };
        Host(new Border { Child = text });

        Assert.That(text.Layout.LineCount, Is.EqualTo(3));
    }

    [Test]
    public void MaxLines_InSpacedColumns_ShowsThemAll()
    {
        var text = new TextBlock
        {
            Text = Prose + " " + Prose, Width = 400, TextWrapping = TextWrapping.WrapByWords, Columns = 2, MaxLines = 3,
            LineSpacing = 6,
        };
        Host(new Border { Child = text });

        Assert.That(text.Layout.LineCount, Is.EqualTo(3));
    }

    [Test]
    public void ATextBox_TakesTheLineHeight()
    {
        var plain = new TextBox { Text = "one\ntwo" };
        var box = new TextBox { Text = "one\ntwo", LineHeight = 32 };
        var empty = new TextBox { LineHeight = 32 };

        Assert.That(plain.MeasureSurface(300).Height, Is.LessThan(60));
        Assert.That(box.MeasureSurface(300).Height, Is.EqualTo(64).Within(1e-6));
        Assert.That(empty.MeasureSurface(300).Height, Is.EqualTo(32).Within(1e-6), "an empty field's one line too");
    }
}
