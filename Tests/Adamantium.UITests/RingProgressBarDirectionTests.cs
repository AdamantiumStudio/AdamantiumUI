using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Shapes;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Templates;
using NUnit.Framework;

namespace Adamantium.UITests;

// The ring's native sweep is clockwise (y down), so CounterClockwise is the direction that gets the vertical flip.
[TestFixture]
public class RingProgressBarDirectionTests
{
    private static Ellipse _indicator;

    private static ControlTemplate RingTemplate() => new(() =>
    {
        _indicator = new Ellipse();
        var result = new TemplateResult { RootComponent = _indicator };
        result.RegisterName("PART_Indicator", _indicator);
        return result;
    });

    private static Ellipse Ring(SweepDirection direction, RingStartPosition start = RingStartPosition.Right)
    {
        var ring = new RingProgressBar { Direction = direction, StartPosition = start, Value = 63 };
        ring.Template = RingTemplate();
        return _indicator;
    }

    [Test]
    public void Clockwise_IsTheNativeWinding_AndIsNotMirrored()
    {
        var arc = Ring(SweepDirection.Clockwise);

        Assert.That(arc.RenderTransform, Is.Not.Null, "the arc is placed by a transform");
        Assert.That(arc.RenderTransform.ScaleY, Is.EqualTo(1.0).Within(0.001),
            "0..90 already sweeps downward from 3 o'clock - mirroring it would draw the other direction");
    }

    [Test]
    public void CounterClockwise_IsTheMirroredOne()
    {
        var arc = Ring(SweepDirection.CounterClockwise);

        Assert.That(arc.RenderTransform.ScaleY, Is.EqualTo(-1.0).Within(0.001),
            "the arc cannot sweep backwards, so the opposite direction is a vertical flip");
    }

    // The flip and the rotation are independent: the start position must not change which direction is mirrored, and the
    // mirror must not move the start (3 o'clock sits ON the flip axis, which is why this composition works at all).
    [Test]
    public void TheStartPositionDoesNotChangeWhichDirectionIsMirrored()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Ring(SweepDirection.Clockwise, RingStartPosition.Top).RenderTransform.ScaleY,
                Is.EqualTo(1.0).Within(0.001));
            Assert.That(Ring(SweepDirection.CounterClockwise, RingStartPosition.Top).RenderTransform.ScaleY,
                Is.EqualTo(-1.0).Within(0.001));
            Assert.That(Ring(SweepDirection.Clockwise, RingStartPosition.Top).RenderTransform.RotationAngle,
                Is.EqualTo(270.0).Within(0.001), "and the start rotation is the same whichever way it winds");
        });
    }
}
