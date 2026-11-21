using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry.Shapes;
using Adamantium.UI.Controls.Shapes;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using NUnit.Framework;

namespace Adamantium.UITests;

// An ellipse's angles are kept as written - any start, a sweep either way and of any size - and drawn and hit as the part
// of the ellipse they cover (ArcSpan).
[TestFixture]
public class EllipseAngleTests
{
    [TestCase(270, 45)]
    [TestCase(-90, -45)]
    [TestCase(450, 720)]
    public void TheAngles_AreKeptAsWritten(double start, double sweep)
    {
        var arc = new Ellipse { StartAngle = start, SweepAngle = sweep };

        Assert.Multiple(() =>
        {
            Assert.That(arc.StartAngle, Is.EqualTo(start));
            Assert.That(arc.SweepAngle, Is.EqualTo(sweep));
            Assert.That(arc.Span, Is.EqualTo(new ArcSpan(start, sweep)), "drawn as the part they cover");
        });
    }

    // A 100 x 100 ellipse, laid out: its center at 50, 50, y down.
    private static Ellipse Laid(double start, double sweep, EllipseType type = EllipseType.Sector, double ring = 0,
        bool filled = true)
    {
        var arc = new Ellipse
        {
            StartAngle = start, SweepAngle = sweep, EllipseType = type, RingThickness = ring,
            Fill = filled ? Brushes.White : null, Stroke = filled ? null : Brushes.White, StrokeThickness = 4,
            Width = 100, Height = 100
        };
        arc.Measure(new Size<double>(100, 100));
        arc.Arrange(new Rect(0, 0, 100, 100));
        return arc;
    }

    private static bool Hits(Ellipse arc, double x, double y) => arc.HitTestCore(new Vector2<double>(x, y));

    // From twelve to three: the top right quarter, written forwards from -90 or 270, or backwards from 0 - or past a turn,
    // the quarter left over it.
    [TestCase(-90, 90)]
    [TestCase(0, -90)]
    [TestCase(270, 90)]
    [TestCase(270, 450)]
    [TestCase(0, -450)]
    public void ASector_IsHitInItsWedgeOnly(double start, double sweep)
    {
        var arc = Laid(start, sweep);

        Assert.Multiple(() =>
        {
            Assert.That(Hits(arc, 75, 25), Is.True, "top right");
            Assert.That(Hits(arc, 25, 75), Is.False, "bottom left");
            Assert.That(Hits(arc, 25, 25), Is.False, "top left");
            Assert.That(Hits(arc, 75, 75), Is.False, "bottom right");
        });
    }

    // An edge-to-edge quarter is the ellipse beyond its chord: the corner of the quarter by the center is not in it.
    [Test]
    public void ASegment_IsHitBeyondItsChordOnly()
    {
        var segment = Laid(0, 90, EllipseType.EdgeToEdge);
        var sector = Laid(0, 90);

        Assert.Multiple(() =>
        {
            Assert.That(Hits(segment, 80, 80), Is.True, "out by the rim");
            Assert.That(Hits(segment, 58, 58), Is.False, "the chord's center side");
            Assert.That(Hits(sector, 58, 58), Is.True, "which the sector holds");
        });
    }

    [Test]
    public void ARing_IsNotHitInItsHole()
    {
        var ring = Laid(0, 360, ring: 10);

        Assert.Multiple(() =>
        {
            Assert.That(Hits(ring, 50, 4), Is.True, "the band");
            Assert.That(Hits(ring, 50, 50), Is.False, "the hole");
        });
    }

    [Test]
    public void AnOutlinedArc_IsHitAlongItsArcOnly()
    {
        var arc = Laid(0, 90, filled: false);

        Assert.Multiple(() =>
        {
            Assert.That(Hits(arc, 50 + 49 * 0.7071, 50 + 49 * 0.7071), Is.True, "on the outline, in the sweep");
            Assert.That(Hits(arc, 50 - 49 * 0.7071, 50 - 49 * 0.7071), Is.False, "on the outline the arc does not go round");
            Assert.That(Hits(arc, 60, 60), Is.False, "inside, hollow");
        });
    }

    // The pen strokes a sector back through the center: its radii are outline too. An edge-to-edge arc is open.
    [Test]
    public void AnOutlinedSector_IsHitOnItsRadii_AnArcIsNot()
    {
        var sector = Laid(0, 90, filled: false);
        var arc = Laid(0, 90, EllipseType.EdgeToEdge, filled: false);

        Assert.Multiple(() =>
        {
            Assert.That(Hits(sector, 75, 50), Is.True, "on the radius at three o'clock");
            Assert.That(Hits(sector, 50, 75), Is.True, "and the one at six");
            Assert.That(Hits(arc, 75, 50), Is.False, "an arc has no radius");
        });
    }

    // The band is drawn inside the outline OnRender draws, the stroke's half width in from the box: 48 out, the hole
    // 10 in from that. Measured from the box, the hole took a pixel's ring of painted band.
    [Test]
    public void ARingsHole_IsWhereItIsDrawn()
    {
        var ring = Laid(0, 360, ring: 10);

        Assert.Multiple(() =>
        {
            Assert.That(Hits(ring, 50, 50 - 39), Is.True, "39 out: the band");
            Assert.That(Hits(ring, 50, 50 - 37), Is.False, "37 out: the hole");
        });
    }

    // An outlined ring slice is stroked round its band: the outer arc, the inner one and the two ends between them - not
    // across the band's inside.
    [Test]
    public void AnOutlinedRingSlice_IsHitRoundItsBand()
    {
        var slice = Laid(0, 90, ring: 10, filled: false);
        const double diagonal = 0.7071;

        Assert.Multiple(() =>
        {
            Assert.That(Hits(slice, 50 + 48 * diagonal, 50 + 48 * diagonal), Is.True, "the outer arc");
            Assert.That(Hits(slice, 50 + 38 * diagonal, 50 + 38 * diagonal), Is.True, "the inner arc");
            Assert.That(Hits(slice, 50 + 43, 50), Is.True, "the end at three o'clock");
            Assert.That(Hits(slice, 50, 50 + 43), Is.True, "the end at six");
            Assert.That(Hits(slice, 50 + 43 * diagonal, 50 + 43 * diagonal), Is.False, "inside the band");
            Assert.That(Hits(slice, 50 + 20, 50), Is.False, "nothing on to the center");
        });
    }

    [Test]
    public void NoSweep_IsNotHit() => Assert.That(Hits(Laid(30, 0), 50, 50), Is.False);
}
