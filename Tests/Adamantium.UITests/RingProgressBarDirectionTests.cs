using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Shapes;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Templates;
using NUnit.Framework;

namespace Adamantium.UITests;

// The ring's arc is placed by its own angles: it starts at StartPosition and sweeps Direction's way round, a negative
// sweep counterclockwise - no transform turning or mirroring it.
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

    private static RingProgressBar Ring(SweepDirection direction, RingStartPosition start, double value = 25)
    {
        var ring = new RingProgressBar { Direction = direction, StartPosition = start, Value = value };
        ring.Template = RingTemplate();
        return ring;
    }

    [TestCase(RingStartPosition.Top, SweepDirection.Clockwise, 270, 90)]
    [TestCase(RingStartPosition.Right, SweepDirection.Clockwise, 0, 90)]
    [TestCase(RingStartPosition.Bottom, SweepDirection.Clockwise, 90, 90)]
    [TestCase(RingStartPosition.Left, SweepDirection.Clockwise, 180, 90)]
    [TestCase(RingStartPosition.Top, SweepDirection.CounterClockwise, 270, -90)]
    [TestCase(RingStartPosition.Right, SweepDirection.CounterClockwise, 0, -90)]
    [TestCase(RingStartPosition.Bottom, SweepDirection.CounterClockwise, 90, -90)]
    [TestCase(RingStartPosition.Left, SweepDirection.CounterClockwise, 180, -90)]
    public void TheArc_StartsAtItsPosition_AndSweepsItsWay(RingStartPosition start, SweepDirection direction,
        double startAngle, double sweepAngle)
    {
        Ring(direction, start);

        Assert.Multiple(() =>
        {
            Assert.That(_indicator.StartAngle, Is.EqualTo(startAngle));
            Assert.That(_indicator.SweepAngle, Is.EqualTo(sweepAngle));
            Assert.That(_indicator.RenderTransform, Is.Null, "nothing turns or mirrors it");
        });
    }

    // Full is the whole ring either way; empty is nothing, 0 and not -0.
    [TestCase(SweepDirection.Clockwise, 100, 360)]
    [TestCase(SweepDirection.CounterClockwise, 100, -360)]
    [TestCase(SweepDirection.CounterClockwise, 0, 0)]
    public void FullAndEmpty(SweepDirection direction, double value, double sweepAngle)
    {
        Ring(direction, RingStartPosition.Top, value);

        Assert.Multiple(() =>
        {
            Assert.That(_indicator.SweepAngle, Is.EqualTo(sweepAngle));
            Assert.That(double.IsNegative(_indicator.SweepAngle), Is.EqualTo(sweepAngle < 0), "its sign");
        });
    }

    // Changed after the template is applied, the arc follows at once.
    [Test]
    public void ChangingThePositionOrTheWay_MovesTheArc()
    {
        var ring = Ring(SweepDirection.Clockwise, RingStartPosition.Right);

        ring.StartPosition = RingStartPosition.Top;
        ring.Direction = SweepDirection.CounterClockwise;

        Assert.Multiple(() =>
        {
            Assert.That(_indicator.StartAngle, Is.EqualTo(270));
            Assert.That(_indicator.SweepAngle, Is.EqualTo(-90));
        });
    }
}
