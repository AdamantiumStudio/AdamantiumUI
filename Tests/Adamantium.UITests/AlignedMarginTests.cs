using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using NUnit.Framework;

namespace Adamantium.UITests;

// A margin holds a child off whichever edge it aligns to, identically for Border and Ellipse, in several track shapes
// and across a live alignment flip.
[TestFixture]
public class AlignedMarginTests
{
    private static Rect Placed(HorizontalAlignment alignment, double cornerRadius = 0)
    {
        var child = new Border { Width = 18, Height = 18, Margin = new Thickness(2) };
        child.HorizontalAlignment = alignment;
        child.VerticalAlignment = VerticalAlignment.Center;

        var host = new Grid();
        ((IContainer)host).AddOrSetChildComponent(child);

        // The switch's thumb sits in a Grid inside a ROUNDED Border, which is the one thing the first version of this
        // test left out.
        var track = new Border
        {
            Width = 38, Height = 22,
            CornerRadius = new CornerRadius(cornerRadius),
            Child = host
        };

        var root = new Border { Width = 200, Height = 100, Child = track };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(root);

        return child.Bounds;
    }

    private static Rect PlacedEllipse(HorizontalAlignment alignment)
    {
        var child = new Adamantium.UI.Controls.Shapes.Ellipse
        {
            Width = 18, Height = 18, Margin = new Thickness(2),
            Fill = new Adamantium.UI.Core.Media.SolidColorBrush(Colors.White)
        };
        child.HorizontalAlignment = alignment;
        child.VerticalAlignment = VerticalAlignment.Center;

        var host = new Grid();
        ((IContainer)host).AddOrSetChildComponent(child);

        var track = new Border { Width = 38, Height = 22, CornerRadius = new CornerRadius(11), Child = host };
        var root = new Border { Width = 200, Height = 100, Child = track };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(root);

        return child.Bounds;
    }

    // A 38x22 track with a 1px border, leaving a 36x20 box too short for an 18 thumb with 2 all round. Bounds are
    // parent-local, so they read against the 36-wide inner box.
    private static Rect InOutlinedTrack(MeasurableUIComponent thumb, HorizontalAlignment alignment)
    {
        thumb.Width = 18;
        thumb.Height = 18;
        thumb.Margin = new Thickness(2);
        thumb.HorizontalAlignment = alignment;
        thumb.VerticalAlignment = VerticalAlignment.Center;

        var host = new Grid();
        ((IContainer)host).AddOrSetChildComponent(thumb);
        var track = new Border
        {
            Width = 38, Height = 22,
            CornerRadius = new CornerRadius(11),
            BorderThickness = new Thickness(1),
            BorderBrush = new Adamantium.UI.Core.Media.SolidColorBrush(Colors.Black),
            Child = host
        };
        var root = new Border { Width = 200, Height = 100, Child = track };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(root);

        return thumb.Bounds;
    }

    [Test]
    public void AnOverConstrainedThumbKeepsItsSizeAndItsInset()
    {
        var borderLeft = InOutlinedTrack(new Border(), HorizontalAlignment.Left);
        var borderRight = InOutlinedTrack(new Border(), HorizontalAlignment.Right);
        var ellipseLeft = InOutlinedTrack(new Adamantium.UI.Controls.Shapes.Ellipse
            { Fill = new Adamantium.UI.Core.Media.SolidColorBrush(Colors.White) }, HorizontalAlignment.Left);
        var ellipseRight = InOutlinedTrack(new Adamantium.UI.Controls.Shapes.Ellipse
            { Fill = new Adamantium.UI.Core.Media.SolidColorBrush(Colors.White) }, HorizontalAlignment.Right);

        TestContext.WriteLine($"border  left={borderLeft}  right={borderRight}");
        TestContext.WriteLine($"ellipse left={ellipseLeft}  right={ellipseRight}");

        // 2 in from either end of the 36-wide inner box, and the explicit 18x18 survives the squeeze - Width/Height are
        // re-applied AFTER the alignment clamp, so the thumb overflows the short box by a hair rather than being
        // flattened. Identical for both primitives: whatever made the switch look wrong, it was not the Ellipse.
        Assert.Multiple(() =>
        {
            Assert.That(borderLeft.X, Is.EqualTo(2).Within(0.01), "border, left");
            Assert.That(36 - borderRight.Right, Is.EqualTo(2).Within(0.01), "border, right");
            Assert.That(ellipseLeft.X, Is.EqualTo(2).Within(0.01), "ellipse, left");
            Assert.That(36 - ellipseRight.Right, Is.EqualTo(2).Within(0.01), "ellipse, right");
            Assert.That(ellipseLeft, Is.EqualTo(borderLeft), "same box, same placement");
            Assert.That(ellipseRight, Is.EqualTo(borderRight), "at either end");
        });
    }

    [Test]
    public void AnEllipseIsHeldOffBothEdgesEqually()
    {
        var left = PlacedEllipse(HorizontalAlignment.Left);
        var right = PlacedEllipse(HorizontalAlignment.Right);

        TestContext.WriteLine($"ellipse left-aligned: x={left.X} right={left.Right} w={left.Width}");
        TestContext.WriteLine($"ellipse right-aligned: x={right.X} right={right.Right} w={right.Width}");

        Assert.Multiple(() =>
        {
            Assert.That(left.X, Is.EqualTo(2).Within(0.01), "left-aligned");
            Assert.That(38 - right.Right, Is.EqualTo(2).Within(0.01), "right-aligned");
        });
    }

    /// <summary>The switch does not BUILD its thumb at an end - it MOVES it there, on a tree that is already laid out.
    /// Everything above sets the alignment before the first pass, which is the one path the real control never takes.
    /// </summary>
    [Test]
    public void FlippingAlignmentOnALaidOutTreeMovesTheChildAllTheWay()
    {
        var child = new Border { Width = 18, Height = 18, Margin = new Thickness(2) };
        child.HorizontalAlignment = HorizontalAlignment.Left;
        child.VerticalAlignment = VerticalAlignment.Center;

        var host = new Grid();
        ((IContainer)host).AddOrSetChildComponent(child);
        var track = new Border { Width = 38, Height = 22, CornerRadius = new CornerRadius(11), Child = host };
        var root = new Border { Width = 200, Height = 100, Child = track };

        Adamantium.UI.Extensions.WindowExtension.UpdateTree(root);
        var off = child.Bounds;

        child.HorizontalAlignment = HorizontalAlignment.Right;
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(root);
        var on = child.Bounds;

        TestContext.WriteLine($"off: x={off.X} right={off.Right}");
        TestContext.WriteLine($"on:  x={on.X} right={on.Right}");

        Assert.Multiple(() =>
        {
            Assert.That(off.X, Is.EqualTo(2).Within(0.01), "off");
            Assert.That(38 - on.Right, Is.EqualTo(2).Within(0.01), "on, after a live flip");
        });
    }

    [Test]
    public void AMarginHoldsAChildOffBothEdgesEqually()
    {
        var left = Placed(HorizontalAlignment.Left, cornerRadius: 11);
        var right = Placed(HorizontalAlignment.Right, cornerRadius: 11);

        TestContext.WriteLine($"left-aligned: x={left.X} right={left.Right} w={left.Width}");
        TestContext.WriteLine($"right-aligned: x={right.X} right={right.Right} w={right.Width}");

        // 38 wide, an 18 child with a 2 margin: 2 in from whichever edge it is aligned to.
        Assert.Multiple(() =>
        {
            Assert.That(left.X, Is.EqualTo(2).Within(0.01), "left-aligned: the margin holds it off the left edge");
            Assert.That(38 - right.Right, Is.EqualTo(2).Within(0.01), "right-aligned: and off the right one");
        });
    }
}
