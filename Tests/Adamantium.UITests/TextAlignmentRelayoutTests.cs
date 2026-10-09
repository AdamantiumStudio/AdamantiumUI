using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using NUnit.Framework;
using HorizontalTextAlignment = Adamantium.Graphics.Fonts.HorizontalTextAlignment;
using TextTrimming = Adamantium.Graphics.Fonts.TextTrimming;

namespace Adamantium.UITests;

// Right-aligned, trimmed text stands at the right edge of the slot it was GIVEN, whatever width it was measured against:
// the detail column of a list whose rows are reused stood short of the edge, by as much as the row's old label was wider.
public class TextAlignmentRelayoutTests
{
    private static void Settle(Window window)
    {
        for (var i = 0; i < 6; i++) Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
    }

    private static TextBlock Detail() => new()
    {
        Text = "Brush", FontSize = 14, HorizontalTextAlignment = HorizontalTextAlignment.Right,
        TextTrimming = TextTrimming.CharEllipses
    };

    private static double RightEdge(TextBlock block) => block.Layout.GetCaretStops().Last().X;

    [TestCase("Magenta", "Blue")]
    [TestCase("Blue", "Magenta")]
    public void RightAlignedText_StaysAtTheRightEdge_WhenItsColumnChanges(string before, string after)
    {
        var label = new TextBlock { Text = before, FontSize = 14 };
        var detail = Detail();
        Grid.SetColumn(detail, 1);
        var grid = new Grid { Width = 440 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(label);
        grid.Children.Add(detail);
        var window = new Window { Width = 600, Height = 200, Content = grid };
        Settle(window);

        label.Text = after;
        Settle(window);

        Assert.That(RightEdge(detail), Is.EqualTo(detail.RenderSize.Width).Within(1.0));
    }

    // A measure whose size comes out the same has no arrange after it: the layout stayed at the measured width.
    [Test]
    public void AMeasureWithNoArrangeAfterIt_DoesNotMoveRightAlignedText()
    {
        var detail = Detail();
        var window = new Window { Width = 600, Height = 200, Content = new Grid { Width = 440, Children = { detail } } };
        Settle(window);

        detail.Measure(new Size(200, double.PositiveInfinity), force: true);
        Settle(window);

        Assert.That(RightEdge(detail), Is.EqualTo(detail.RenderSize.Width).Within(1.0),
            "the text is aligned to the width it was measured against");
    }
}
