using Adamantium.Mathematics;
using Adamantium.UI.Controls.Canvases;
using Adamantium.UI.Core;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A plane with edges keeps the camera on it: scrolled no further than its edges, centered across where it is
/// narrower than the viewport, at the top where it is shorter; and an endless plane goes wherever it is sent.</summary>
[TestFixture]
public class CanvasPlaneBoundsTests
{
    private static InfiniteCanvas Canvas(Rect plane)
    {
        var canvas = new InfiniteCanvas { PlaneBounds = plane };
        canvas.Measure(new Size<double>(800, 600), force: true);
        canvas.Arrange(new Rect(0, 0, 800, 600));
        return canvas;
    }

    [Test]
    public void AnEndlessPlaneGoesWhereverItIsSent()
    {
        var canvas = Canvas(Rect.Empty);
        canvas.Offset = new Vector2<double>(-5000, 7000);
        Assert.That(canvas.Offset, Is.EqualTo(new Vector2<double>(-5000, 7000)));
        Assert.That(canvas.HasPlaneBounds, Is.False);
    }

    [Test]
    public void APlaneLargerThanTheViewportIsScrolledNoFurtherThanItsEdges()
    {
        // 2000 × 3000 at 1:1 in 800 × 600.
        var canvas = Canvas(new Rect(0, 0, 2000, 3000));
        canvas.Offset = new Vector2<double>(500, 400);
        Assert.That(canvas.Offset, Is.EqualTo(new Vector2<double>(0, 0)), "its top left corner at the viewport's");

        canvas.Offset = new Vector2<double>(-9000, -9000);
        Assert.That(canvas.Offset, Is.EqualTo(new Vector2<double>(800 - 2000, 600 - 3000)), "its bottom right at the viewport's");

        canvas.Offset = new Vector2<double>(-300, -1000);
        Assert.That(canvas.Offset, Is.EqualTo(new Vector2<double>(-300, -1000)), "anywhere between is free");
    }

    [Test]
    public void ANarrowShortPlaneStandsCenteredAcrossAndAtTheTop()
    {
        // 400 × 300 at 1:1 in 800 × 600: centered across, at the top down, and it does not move at all.
        var canvas = Canvas(new Rect(100, 50, 400, 300));
        canvas.Offset = new Vector2<double>(-777, 999);
        Assert.That(canvas.Offset.X, Is.EqualTo(200 - 100).Within(1e-9));
        Assert.That(canvas.Offset.Y, Is.EqualTo(-50).Within(1e-9));
    }

    [Test]
    public void ZoomingOutPastThePlanesSizeCentersIt()
    {
        var canvas = Canvas(new Rect(0, 0, 2000, 1000));
        canvas.Offset = new Vector2<double>(-1000, -200);
        canvas.Scale = 0.25;   // 500 × 250 on screen

        Assert.That(canvas.Offset.X, Is.EqualTo((800 - 500) / 2.0).Within(1e-6));
        Assert.That(canvas.Offset.Y, Is.EqualTo(0).Within(1e-6));
    }

    [Test]
    public void EdgesGivenLaterPullTheCameraOntoThePlane()
    {
        var canvas = Canvas(Rect.Empty);
        canvas.Offset = new Vector2<double>(5000, 5000);
        canvas.PlaneBounds = new Rect(0, 0, 2000, 2000);
        Assert.That(canvas.Offset, Is.EqualTo(new Vector2<double>(0, 0)));
    }

    [Test]
    public void AViewportThatGrowsKeepsThePlaneOnIt()
    {
        var canvas = Canvas(new Rect(0, 0, 2000, 3000));
        canvas.Offset = new Vector2<double>(-1200, -2400);   // the bottom right corner in an 800 × 600 window

        canvas.Measure(new Size<double>(1000, 900), force: true);
        canvas.Arrange(new Rect(0, 0, 1000, 900));
        Assert.That(canvas.Offset, Is.EqualTo(new Vector2<double>(1000 - 2000, 900 - 3000)), "pressed to the corner again");
    }

    [Test]
    public void EdgesTakenAwayLeaveTheCameraWhereItWasShown()
    {
        var canvas = Canvas(new Rect(0, 0, 2000, 3000));
        canvas.Offset = new Vector2<double>(9000, 9000);   // asked for, shown at the corner
        canvas.PlaneBounds = Rect.Empty;
        Assert.That(canvas.Offset, Is.EqualTo(new Vector2<double>(0, 0)), "not back at what was asked before");
    }

    [Test]
    public void AScaleTypedInStaysOnThePlane()
    {
        var canvas = Canvas(new Rect(0, 0, 2000, 3000));
        canvas.Offset = new Vector2<double>(-1200, -2400);
        canvas.Scale = 2;   // the middle is held - and the plane still covers the window
        var offset = canvas.Offset;
        Assert.That(offset.X, Is.InRange(800 - 4000.0, 0));
        Assert.That(offset.Y, Is.InRange(600 - 6000.0, 0));
    }

    [Test]
    public void BeforeAViewportTheCameraIsLeftAsWritten()
    {
        var canvas = new InfiniteCanvas { PlaneBounds = new Rect(0, 0, 100, 100) };
        canvas.Offset = new Vector2<double>(-500, 700);
        Assert.That(canvas.Offset, Is.EqualTo(new Vector2<double>(-500, 700)));
    }

    [Test]
    public void TheWheelZoomsByDefault()
    {
        Assert.That(new InfiniteCanvas().WheelMode, Is.EqualTo(CanvasWheelMode.Zoom));
    }
}
