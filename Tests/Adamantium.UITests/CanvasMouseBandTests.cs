using Adamantium.Mathematics;
using Adamantium.UI.Controls.Canvases;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Media.Animation;
using Adamantium.UI.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A mouse dragging a plane with edges pulls it past them on a band, as a finger does: held there, it stays
/// stretched; let go, it springs back; thrown, it coasts through the band and back.</summary>
public class CanvasMouseBandTests
{
    private const uint Step = 10;

    [SetUp]
    public void FreshClock() => AnimationManager.Reset();

    [TearDown]
    public void LetThePointerGo()
    {
        Mouse.Pointer.Capture(null);
        Mouse.Pointer.ScreenPosition = default;
        AnimationManager.Reset();
    }

    private static InfiniteCanvas Sized(Rect plane)
    {
        var canvas = new InfiniteCanvas { Scene = new CanvasScene(), Width = 400, Height = 300, PlaneBounds = plane };
        var holder = new Canvas();
        holder.Children.Add(canvas);

        var root = new VisualRoot(holder, 400, 300);
        ((IMeasurableComponent)root).Measure(new Size<double>(400, 300));
        ((IMeasurableComponent)root).Arrange(new Rect(0, 0, 400, 300));

        return canvas;
    }

    private static readonly Rect Plane = new(0, 0, 2000, 2000);

    private static void Frames(int count)
    {
        for (var i = 0; i < count; i++) AnimationManager.Tick(1 / 60.0);
    }

    private static void Press(InfiniteCanvas canvas, int x, uint at)
    {
        var held = InputModifiers.MiddleMouseButton;
        Mouse.Pointer.ScreenPosition = new PixelPoint(x, 100);
        canvas.RaiseEvent(new PointerPressedEventArgs(Mouse.Pointer, MouseButtons.Middle, held, at)
        {
            RoutedEvent = PointerEvents.PointerPressedEvent,
            Properties = PointerPointProperties.FromMouse(held)
        });
    }

    private static void MoveTo(InfiniteCanvas canvas, int x, uint at, int y = 100)
    {
        var held = InputModifiers.MiddleMouseButton;
        Mouse.Pointer.ScreenPosition = new PixelPoint(x, y);
        canvas.RaiseEvent(new PointerEventArgs(Mouse.Pointer, held, at)
        {
            RoutedEvent = PointerEvents.PointerMovedEvent,
            Properties = PointerPointProperties.FromMouse(held)
        });
    }

    private static void Release(InfiniteCanvas canvas, uint at)
    {
        canvas.RaiseEvent(new PointerReleasedEventArgs(Mouse.Pointer, MouseButtons.Middle, InputModifiers.None, at)
        {
            RoutedEvent = PointerEvents.PointerReleasedEvent,
            Properties = PointerPointProperties.FromMouse(InputModifiers.None)
        });
    }

    // Pulled from x = 100 rightwards - past the plane's left edge, where the camera stands at 0 - in five moves.
    private static uint PullRight(InfiniteCanvas canvas, int by)
    {
        uint at = 1000;
        Press(canvas, 100, at);

        for (var i = 1; i <= 5; i++)
        {
            at += Step;
            MoveTo(canvas, 100 + i * by / 5, at);
        }

        return at;
    }

    [Test]
    public void ADragPastAnEdgeStretchesThePlaneLessThanTheMouseMoved()
    {
        var canvas = Sized(Plane);
        PullRight(canvas, 200);

        Assert.That(canvas.Offset.X, Is.GreaterThan(0).And.LessThan(200), "past the edge, held back by the band");
        Assert.That(canvas.Offset.Y, Is.EqualTo(0));
    }

    [Test]
    public void AStretchLetGoStillSpringsBackToTheEdge()
    {
        var canvas = Sized(Plane);
        var at = PullRight(canvas, 200);
        var stretched = canvas.Offset.X;

        Release(canvas, at + 500);   // held still a while first: no throw
        Frames(3);
        var springing = canvas.Offset.X;
        Frames(120);

        Assert.Multiple(() =>
        {
            Assert.That(springing, Is.LessThan(stretched).And.GreaterThan(0), "on its way back");
            Assert.That(canvas.Offset.X, Is.EqualTo(0).Within(1e-9), "and at the edge");
        });
    }

    [Test]
    public void AThrowAtAnEdgeCoastsThroughTheBandAndBack()
    {
        var canvas = Sized(Plane);
        canvas.Offset = new Vector2<double>(-50, 0);
        var at = PullRight(canvas, 60);   // ends 10 past the edge, still moving

        Release(canvas, at);
        var letGo = canvas.Offset.X;
        Frames(10);
        var coasted = canvas.Offset.X;
        Frames(300);

        Assert.Multiple(() =>
        {
            Assert.That(coasted, Is.GreaterThan(letGo), "the throw went on past the edge");
            Assert.That(canvas.Offset.X, Is.EqualTo(0).Within(1e-9), "and came back to it");
        });
    }

    // Pulled fast and let go while still moving: out a little, turned at once and back - no creeping outwards first.
    [TestCase(-50, 60)]
    [TestCase(0, 200)]
    public void AThrowPastAnEdgeRunsOutAndComesBackInOneMotion(int from, int pull)
    {
        var canvas = Sized(Plane);
        canvas.Offset = new Vector2<double>(from, 0);
        var at = PullRight(canvas, pull);
        Release(canvas, at);
        var letGo = canvas.Offset.X;

        var path = new System.Collections.Generic.List<double>();
        for (var i = 0; i < 45; i++)
        {
            Frames(1);
            path.Add(canvas.Offset.X);
        }

        var peak = path.IndexOf(System.Linq.Enumerable.Max(path));
        Assert.Multiple(() =>
        {
            Assert.That(peak, Is.LessThan(12), "it turned within a fifth of a second");
            Assert.That(path[peak], Is.LessThanOrEqualTo(letGo + 0.12 * 400 + 1), "and not far");
            for (var i = peak + 1; i < path.Count; i++)
                Assert.That(path[i], Is.LessThanOrEqualTo(path[i - 1]), $"straight back, frame {i}");
            Assert.That(canvas.Offset.X, Is.EqualTo(0).Within(1e-9), "at the edge in three quarters of a second");
        });
    }

    // Flicked from inside the plane into its edge, fast: out no farther than the share of the window, and back.
    [Test]
    public void AFlickIntoAnEdgeFromThePlaneRunsOutNoFartherThanItsShare()
    {
        var canvas = Sized(Plane);
        canvas.Offset = new Vector2<double>(-200, 0);
        var at = PullRight(canvas, 200);   // 8000 px/s, at the edge as it is let go
        Assume.That(canvas.Offset.X, Is.EqualTo(0).Within(1e-9));
        Release(canvas, at);

        var farthest = 0.0;
        for (var i = 0; i < 60; i++)
        {
            Frames(1);
            farthest = System.Math.Max(farthest, canvas.Offset.X);
        }

        Assert.Multiple(() =>
        {
            Assert.That(farthest, Is.GreaterThan(5).And.LessThanOrEqualTo(0.12 * 400 + 1));
            Assert.That(canvas.Offset.X, Is.EqualTo(0).Within(1e-9));
        });
    }

    // Let go while stretched and moving: the plane goes on at the speed it was SHOWN moving, which the band slowed -
    // not at the hand's, which would kick it outwards.
    [Test]
    public void LettingGoPastAnEdgeDoesNotKickThePlaneOutwards()
    {
        var canvas = Sized(Plane);
        uint at = 1000;
        Press(canvas, 100, at);
        var before = 0.0;
        for (var i = 1; i <= 5; i++)
        {
            before = canvas.Offset.X;
            MoveTo(canvas, 100 + i * 40, at += Step);
        }

        var shownSpeed = (canvas.Offset.X - before) / (Step / 1000.0);
        Release(canvas, at);
        var letGo = canvas.Offset.X;
        Frames(1);

        Assert.That((canvas.Offset.X - letGo) * 60, Is.LessThanOrEqualTo(shownSpeed * 1.05));
    }

    // Stretched, then thrown back in: it crosses the edge at speed and coasts on over the plane.
    [Test]
    public void AThrowBackInFromAStretchCoastsOnOverThePlane()
    {
        var canvas = Sized(Plane);
        var at = PullRight(canvas, 100);
        for (var i = 1; i <= 5; i++) MoveTo(canvas, 200 - i * 60, at += Step);
        Assume.That(canvas.Offset.X, Is.GreaterThan(-200), "let go near the edge");
        var letGo = canvas.Offset.X;
        Release(canvas, at);

        Frames(120);
        Assert.That(canvas.Offset.X, Is.LessThan(letGo - 100), "it went on in, well past where it was let go");
    }

    [Test]
    public void APressWhileSpringingBackCatchesThePlaneWhereItIs()
    {
        var canvas = Sized(Plane);
        var at = PullRight(canvas, 200);
        Release(canvas, at + 500);
        Frames(3);

        var shown = canvas.Offset;
        Assume.That(shown.X, Is.GreaterThan(0), "still past the edge");

        Press(canvas, 300, at + 600);
        Assert.That(canvas.Offset, Is.EqualTo(shown), "no jump to the edge under the hand");

        MoveTo(canvas, 300, at + 610);
        Frames(30);
        Assert.That(canvas.Offset.X, Is.EqualTo(shown.X).Within(1e-9), "and held there while the button is down");

        Release(canvas, at + 1200);
        Frames(120);
        Assert.That(canvas.Offset.X, Is.EqualTo(0).Within(1e-9));
    }

    [Test]
    public void APressThatDoesNotPanLetsTheSpringBackGoOn()
    {
        var canvas = Sized(Plane);
        var at = PullRight(canvas, 200);
        Release(canvas, at + 500);
        Frames(3);

        var shown = canvas.Offset;
        Assume.That(shown.X, Is.GreaterThan(0), "still past the edge");

        canvas.RaiseEvent(new PointerPressedEventArgs(Mouse.Pointer, MouseButtons.Left, InputModifiers.LeftMouseButton, at + 600)
        {
            RoutedEvent = PointerEvents.PointerPressedEvent,
            Properties = PointerPointProperties.FromMouse(InputModifiers.LeftMouseButton)
        });

        Assert.That(canvas.Offset, Is.EqualTo(shown), "no jump to the edge");
        Frames(120);
        Assert.That(canvas.Offset.X, Is.EqualTo(0).Within(1e-9), "and on back to it");
    }

    [Test]
    public void ACaptureTakenAwayMidDragSpringsThePlaneBack()
    {
        var canvas = Sized(Plane);
        PullRight(canvas, 200);
        Assume.That(canvas.Offset.X, Is.GreaterThan(0), "stretched");

        Mouse.Pointer.Capture(null);   // what the window does when the system takes the mouse away
        Frames(120);
        Assert.That(canvas.Offset.X, Is.EqualTo(0).Within(1e-9));

        MoveTo(canvas, 400, 2000);
        Assert.That(canvas.Offset.X, Is.EqualTo(0).Within(1e-9), "and a later move pans nothing");
    }

    [Test]
    public void AlongAnAxisThePlaneFitsTheDragDoesNotStretchIt()
    {
        // 2000 wide, 200 tall in a 400 × 300 window: it scrolls across, and stands at the top down.
        var canvas = Sized(new Rect(0, 0, 2000, 200));
        canvas.Offset = new Vector2<double>(-500, 0);

        uint at = 1000;
        Press(canvas, 100, at);
        for (var i = 1; i <= 5; i++) MoveTo(canvas, 100 - i * 10, at += Step, 100 + i * 20);

        Assert.That(canvas.Offset, Is.EqualTo(new Vector2<double>(-550, 0)));
    }

    [Test]
    public void OnAnEndlessPlaneTheDragFollowsTheMouseExactly()
    {
        var canvas = Sized(Rect.Empty);
        var from = canvas.Offset;
        var at = PullRight(canvas, 200);

        Assert.That(canvas.Offset, Is.EqualTo(from + new Vector2<double>(200, 0)));

        Release(canvas, at + 500);
        Frames(30);
        Assert.That(canvas.Offset, Is.EqualTo(from + new Vector2<double>(200, 0)));
    }

    [Test]
    public void InsideTheEdgesTheDragFollowsTheMouseExactly()
    {
        var canvas = Sized(Plane);
        canvas.Offset = new Vector2<double>(-500, -500);
        PullRight(canvas, 200);

        Assert.That(canvas.Offset, Is.EqualTo(new Vector2<double>(-300, -500)));
    }
}
