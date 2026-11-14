using Adamantium.Mathematics;
using Adamantium.UI.Controls.Canvases;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media.Animation;
using Adamantium.UI.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary><see cref="InfiniteCanvas.FlyTo"/>: the camera flies until a piece of world is wholly in view - no further,
/// the zoom only going down - takes as long however far it goes, keeps to the plane's edges and stops at a hand.</summary>
public class CanvasFlightTests
{
    [SetUp]
    public void FreshClock() => AnimationManager.Reset();

    [TearDown]
    public void StopTheClock() => AnimationManager.Reset();

    private static InfiniteCanvas Sized()
    {
        var canvas = new InfiniteCanvas { Scene = new CanvasScene(), Width = 400, Height = 300 };
        var holder = new Canvas();
        holder.Children.Add(canvas);

        var root = new VisualRoot(holder, 400, 300);
        ((IMeasurableComponent)root).Measure(new Size<double>(400, 300));
        ((IMeasurableComponent)root).Arrange(new Rect(0, 0, 400, 300));

        return canvas;
    }

    private static void Frames(int count)
    {
        for (var i = 0; i < count; i++) AnimationManager.Tick(1 / 60.0);
    }

    private static Vector2<double> Middle(Rect box) => new(box.X + box.Width / 2, box.Y + box.Height / 2);

    private static bool Inside(Rect inner, Rect outer) =>
        inner.X >= outer.X - 1e-6 && inner.Y >= outer.Y - 1e-6 && inner.Right <= outer.Right + 1e-6 &&
        inner.Bottom <= outer.Bottom + 1e-6;

    [Test]
    public void APlaceInView_IsNotFlownTo()
    {
        var canvas = Sized();
        var offset = canvas.Offset;
        canvas.FlyTo(new Rect(100, 100, 50, 40));
        Frames(60);

        Assert.Multiple(() =>
        {
            Assert.That(canvas.Offset, Is.EqualTo(offset));
            Assert.That(canvas.Scale, Is.EqualTo(1.0));
        });
    }

    [Test]
    public void APlaceOutOfView_IsFlownToTheMiddle_OverTime()
    {
        var canvas = Sized();
        var place = new Rect(3000, -2000, 40, 30);
        canvas.FlyTo(place);
        Frames(1);
        var underWay = canvas.Looking;
        Frames(60);

        Assert.Multiple(() =>
        {
            Assert.That(underWay.X, Is.GreaterThan(0).And.LessThan(3020), "it flies, it does not jump");
            Assert.That(canvas.Looking.X, Is.EqualTo(3020).Within(1e-6));
            Assert.That(canvas.Looking.Y, Is.EqualTo(-1985).Within(1e-6));
            Assert.That(canvas.Scale, Is.EqualTo(1.0), "a small place is not zoomed into");
        });
    }

    [Test]
    public void APlacePartlyInView_IsMovedJustFarEnough()
    {
        // The world's origin is in the middle of the viewport: the place stands 30 pixels past its right edge.
        var canvas = Sized();
        var start = canvas.Offset;
        var place = new Rect(170, -20, 60, 40);
        var sameAtOnce = Sized();
        sameAtOnce.BringIntoView(place);

        canvas.FlyTo(place);
        Frames(60);

        Assert.Multiple(() =>
        {
            Assert.That(canvas.Offset.X, Is.EqualTo(start.X - 30).Within(1e-6), "its right edge comes to the viewport's");
            Assert.That(canvas.Offset.Y, Is.EqualTo(start.Y).Within(1e-6), "and nothing moves the other way");
            Assert.That(canvas.Offset.X, Is.EqualTo(sameAtOnce.Offset.X).Within(1e-6), "as BringIntoView puts it");
        });
    }

    [Test]
    public void APlaceTooBigToSee_IsZoomedOutToFit()
    {
        var canvas = Sized();
        var place = new Rect(-500, -500, 2000, 600);
        canvas.FlyTo(place);
        Frames(60);

        Assert.Multiple(() =>
        {
            Assert.That(canvas.Scale, Is.LessThan(1.0));
            Assert.That(Inside(place, canvas.VisibleWorld), Is.True, "it is seen whole");
            Assert.That(canvas.Looking.X, Is.EqualTo(Middle(place).X).Within(1e-6));
            Assert.That(canvas.Looking.Y, Is.EqualTo(Middle(place).Y).Within(1e-6));
        });
    }

    [Test]
    public void AFlight_TakesAsLongHoweverFar()
    {
        var near = Sized();
        var far = Sized();
        near.FlightSeconds = 0.5;
        far.FlightSeconds = 0.5;
        near.FlyTo(new Rect(900, 0, 10, 10));
        far.FlyTo(new Rect(90000, 0, 10, 10));
        Frames(29);
        var nearUnderWay = near.Looking.X;
        var farUnderWay = far.Looking.X;
        Frames(2);

        Assert.Multiple(() =>
        {
            Assert.That(nearUnderWay, Is.LessThan(905), "both are still on the way just before the end");
            Assert.That(farUnderWay, Is.LessThan(90005));
            Assert.That(near.Looking.X, Is.EqualTo(905).Within(1e-6), "and both are there at its end");
            Assert.That(far.Looking.X, Is.EqualTo(90005).Within(1e-6));
        });
    }

    [Test]
    public void NoFlightTime_PutsTheCameraThereAtOnce()
    {
        var canvas = Sized();
        canvas.FlightSeconds = 0;
        canvas.FlyTo(new Rect(1000, 1000, 20, 20));

        Assert.That(canvas.Looking, Is.EqualTo(new Vector2<double>(1010, 1010)));
    }

    [Test]
    public void AFlight_KeepsToThePlanesEdges()
    {
        var canvas = Sized();
        canvas.PlaneBounds = new Rect(0, 0, 2000, 1500);
        canvas.FlyTo(new Rect(1980, 1480, 10, 10));
        Frames(60);

        var seen = canvas.VisibleWorld;
        Assert.Multiple(() =>
        {
            Assert.That(seen.Right, Is.EqualTo(2000).Within(1e-6), "the corner is shown pressed to the edge");
            Assert.That(seen.Bottom, Is.EqualTo(1500).Within(1e-6), "not in the middle, with nothing round it");
            Assert.That(Inside(new Rect(1980, 1480, 10, 10), seen), Is.True);
        });
    }

    [Test]
    public void TheWheelOrTheCameraPlaced_EndsAFlightWhereItIs()
    {
        var wheeled = Sized();
        wheeled.FlyTo(new Rect(5000, 0, 10, 10));
        Frames(5);
        wheeled.ZoomAt(new Vector2<double>(200, 150), 1.0);
        var caught = wheeled.Looking;
        Frames(60);

        var placed = Sized();
        placed.FlyTo(new Rect(5000, 0, 10, 10));
        Frames(5);
        placed.CenterOn(new Vector2<double>(-100, -100));
        Frames(60);

        Assert.Multiple(() =>
        {
            Assert.That(wheeled.Looking.X, Is.EqualTo(caught.X).Within(1e-6), "the wheel took the camera");
            Assert.That(placed.Looking, Is.EqualTo(new Vector2<double>(-100, -100)), "code put it elsewhere");
        });
    }

    [Test]
    public void APlaceInView_StopsAFlightGoingElsewhere()
    {
        var canvas = Sized();
        canvas.FlyTo(new Rect(5000, 0, 10, 10));
        Frames(3);
        var seen = canvas.VisibleWorld;
        var inView = new Rect(seen.X + seen.Width / 2 - 5, seen.Y + seen.Height / 2 - 5, 10, 10);
        canvas.FlyTo(inView);
        Frames(60);

        Assert.That(Inside(inView, canvas.VisibleWorld), Is.True, "the camera stays where the place is in view");
    }

    [Test]
    public void TheOffsetWrittenStraight_EndsAFlight()
    {
        var canvas = Sized();
        canvas.FlyTo(new Rect(5000, 0, 10, 10));
        Frames(3);
        canvas.Offset = new Vector2<double>(10, 20);
        Frames(60);

        Assert.That(canvas.Offset, Is.EqualTo(new Vector2<double>(10, 20)));
    }

    [Test]
    public void APointOutOfView_IsFlownToTheMiddle()
    {
        var canvas = Sized();
        canvas.FlyTo(new Rect(800, -600, 0, 0));
        Frames(60);

        Assert.Multiple(() =>
        {
            Assert.That(canvas.Looking.X, Is.EqualTo(800).Within(1e-6));
            Assert.That(canvas.Looking.Y, Is.EqualTo(-600).Within(1e-6));
            Assert.That(canvas.Scale, Is.EqualTo(1.0), "a pan alone leaves the zoom exactly as it was");
        });
    }

    [Test]
    public void AFlight_StartedAgain_GoesToTheNewPlace()
    {
        var canvas = Sized();
        canvas.FlyTo(new Rect(5000, 0, 10, 10));
        Frames(5);
        canvas.FlyTo(new Rect(-3000, 800, 10, 10));
        Frames(60);

        Assert.That(canvas.Looking.X, Is.EqualTo(-2995).Within(1e-6));
        Assert.That(canvas.Looking.Y, Is.EqualTo(805).Within(1e-6));
    }
}
