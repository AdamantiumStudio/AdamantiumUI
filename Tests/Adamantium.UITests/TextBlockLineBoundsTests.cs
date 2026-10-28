using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Text;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A block whose height is its own text keeps its lines inside it under any vertical alignment, as WinUI's, and
/// <see cref="TextBlock.TextLineBounds"/> trims its box to the capitals and the baseline.</summary>
[TestFixture]
public class TextBlockLineBoundsTests
{
    private static TextBlock Measured(VerticalTextAlignment alignment, TextLineBounds bounds = TextLineBounds.Full)
    {
        var text = new TextBlock
        {
            Text = "Saturn ring",
            FontSize = 40,
            VerticalTextAlignment = alignment,
            TextLineBounds = bounds,
        };
        text.Measure(new Size<double>(500, 500));
        return text;
    }

    [TestCase(VerticalTextAlignment.Center)]
    [TestCase(VerticalTextAlignment.Bottom)]
    public void TheSelectionOfABlockAsHighAsItsText_StaysInsideIt(VerticalTextAlignment alignment)
    {
        var text = Measured(alignment);
        var layout = text.Layout;

        Assert.That(layout.GetLine(0).Top, Is.EqualTo(0), "the text was moved within its own height");
        foreach (var rect in layout.GetRangeRects(0, text.Text.Length))
        {
            Assert.That(rect.Y + rect.Height, Is.LessThanOrEqualTo(text.DesiredSize.Height + 1e-3));
        }
    }

    [Test]
    public void Tight_MeasuresFromTheCapitalsToTheBaseline()
    {
        var full = Measured(VerticalTextAlignment.Top);
        var tight = Measured(VerticalTextAlignment.Top, TextLineBounds.Tight);
        var font = tight.Layout.Font;
        var cap = font.CapsHeight * 40.0 / font.UnitsPerEm;

        Assert.That(tight.DesiredSize.Height, Is.LessThan(full.DesiredSize.Height));
        Assert.That(tight.DesiredSize.Height, Is.InRange(cap, cap + 2));
    }

    [Test]
    public void ChangingTheBounds_MeasuresAgain()
    {
        var text = Measured(VerticalTextAlignment.Top);
        var full = text.DesiredSize.Height;

        text.TextLineBounds = TextLineBounds.TrimToBaseline;
        text.Measure(new Size<double>(500, 500));

        Assert.That(text.DesiredSize.Height, Is.LessThan(full));
    }
}
