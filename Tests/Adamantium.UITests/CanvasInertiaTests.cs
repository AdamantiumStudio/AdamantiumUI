using Adamantium.Mathematics;
using Adamantium.UI.Controls.Canvases;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Rendering;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Media.Animation;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>The plane coasts on after a drag or a pinch let go while moving, and stops at a hand.</summary>
public class CanvasInertiaTests
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

    private static void Raise(InfiniteCanvas canvas, ManipulationState state, uint at,
        Vector2<double> translation = default, double scale = 1, int contacts = 2) =>
        canvas.RaiseEvent(new ManipulationEventArgs(state, InputModifiers.None, at)
        {
            RoutedEvent = Gestures.ManipulationEvent,
            Origin = new Vector2<double>(200, 150),
            Translation = translation,
            Scale = scale,
            Contacts = contacts
        });

    private static uint Throw(InfiniteCanvas canvas, double scale = 1, uint letGoAfter = 0)
    {
        uint at = 1000;
        Raise(canvas, ManipulationState.Started, at);

        for (var i = 0; i < 5; i++)
        {
            at += Step;
            Raise(canvas, ManipulationState.Delta, at, new Vector2<double>(scale == 1 ? 10 : 0, 0), scale);
        }

        Raise(canvas, ManipulationState.Completed, at + letGoAfter);
        return at;
    }

    private static void Frames(int count)
    {
        for (var i = 0; i < count; i++) AnimationManager.Tick(1 / 60.0);
    }

    [Test]
    public void APlaneLetGoWhileMovingCoastsOnAndStops()
    {
        var canvas = Sized();
        Throw(canvas);

        var letGo = canvas.Offset.X;
        Frames(5);
        var moving = canvas.Offset.X;
        Frames(300);
        var rest = canvas.Offset.X;
        Frames(10);

        Assert.Multiple(() =>
        {
            Assert.That(moving, Is.GreaterThan(letGo), "it went on after the hand lifted");
            Assert.That(rest, Is.GreaterThan(moving));
            Assert.That(canvas.Offset.X, Is.EqualTo(rest), "and came to rest");
        });
    }

    [Test]
    public void AMiddleButtonDragLetGoWhileMovingCoasts()
    {
        var canvas = Sized();
        MiddleDrag(canvas, 10);

        var letGo = canvas.Offset.X;
        Frames(10);

        Assert.That(letGo, Is.GreaterThan(0), "the drag moved the plane");
        Assert.That(canvas.Offset.X, Is.GreaterThan(letGo), "and it went on after the button came up");
    }

    [Test]
    public void APlaneHeldStillBeforeLettingGoDoesNotCoast()
    {
        var canvas = Sized();
        Throw(canvas, letGoAfter: 500);

        var letGo = canvas.Offset;
        Frames(30);

        Assert.That(canvas.Offset, Is.EqualTo(letGo));
    }

    [Test]
    public void APressCatchesACoastingPlane()
    {
        var canvas = Sized();
        Throw(canvas);
        Frames(3);

        PointerInput.Press(canvas, MouseButtons.Middle);
        var caught = canvas.Offset;
        Frames(30);
        PointerInput.Release(canvas, MouseButtons.Middle);

        Assert.That(canvas.Offset, Is.EqualTo(caught));
    }

    [Test]
    public void WithInertiaOffThePlaneStopsWhereItIsLetGo()
    {
        var canvas = Sized();
        canvas.IsInertiaEnabled = false;
        Throw(canvas);

        var letGo = canvas.Offset;
        Frames(30);

        Assert.That(canvas.Offset, Is.EqualTo(letGo));
    }

    [Test]
    public void APinchLetGoWhileSpreadingCoastsTheZoomAboutTheFingers()
    {
        var canvas = Sized();
        var fingers = new Vector2<double>(200, 150);
        Throw(canvas, scale: 1.05);

        var letGo = canvas.Scale;
        var under = canvas.ScreenToWorld(fingers);
        Frames(120);

        Assert.Multiple(() =>
        {
            Assert.That(canvas.Scale, Is.GreaterThan(letGo), "the zoom went on");
            Assert.That(canvas.WorldToScreen(under).X, Is.EqualTo(fingers.X).Within(1e-6), "about the fingers");
            Assert.That(canvas.WorldToScreen(under).Y, Is.EqualTo(fingers.Y).Within(1e-6));
        });
    }

    [Test]
    public void APinchEndedOnOneFingerCoastsNoZoom()
    {
        var canvas = Sized();
        uint at = 1000;
        Raise(canvas, ManipulationState.Started, at);

        for (var i = 0; i < 5; i++)
        {
            at += Step;
            Raise(canvas, ManipulationState.Delta, at, default, 1.05, contacts: 2);
        }

        at += Step;
        Raise(canvas, ManipulationState.Delta, at, new Vector2<double>(1, 0), 1, contacts: 1);
        Raise(canvas, ManipulationState.Completed, at);

        var letGo = canvas.Scale;
        Frames(60);

        Assert.That(canvas.Scale, Is.EqualTo(letGo));
    }

    [Test]
    public void ADragJustAfterTheWheelThrowsOnlyWhatTheHandDid()
    {
        var canvas = Sized();
        canvas.ZoomAt(new Vector2<double>(50, 50), 2);
        Frames(2);

        // The button held without moving, and let go: whatever the wheel's ease was still doing is not a throw.
        MiddleDrag(canvas, 0, framesBetween: 1);
        var letGo = canvas.Offset;
        Frames(30);

        Assert.That(canvas.Offset, Is.EqualTo(letGo));
    }

    private static void MiddleDrag(InfiniteCanvas canvas, int across, int framesBetween = 0)
    {
        var held = InputModifiers.MiddleMouseButton;
        uint at = 1000;

        Mouse.Pointer.ScreenPosition = new PixelPoint(100, 100);
        canvas.RaiseEvent(new PointerPressedEventArgs(Mouse.Pointer, MouseButtons.Middle, held, at)
        {
            RoutedEvent = PointerEvents.PointerPressedEvent,
            Properties = PointerPointProperties.FromMouse(held)
        });

        for (var i = 1; i <= 5; i++)
        {
            at += Step;
            Frames(framesBetween);
            Mouse.Pointer.ScreenPosition = new PixelPoint(100 + i * across, 100);
            canvas.RaiseEvent(new PointerEventArgs(Mouse.Pointer, held, at)
            {
                RoutedEvent = PointerEvents.PointerMovedEvent,
                Properties = PointerPointProperties.FromMouse(held)
            });
        }

        canvas.RaiseEvent(new PointerReleasedEventArgs(Mouse.Pointer, MouseButtons.Middle, InputModifiers.None, at)
        {
            RoutedEvent = PointerEvents.PointerReleasedEvent,
            Properties = PointerPointProperties.FromMouse(InputModifiers.None)
        });
    }

    [Test]
    public void ACodeMoveOfTheCameraStopsTheCoast()
    {
        var canvas = Sized();
        Throw(canvas);
        Frames(3);

        canvas.CenterOn(Vector2<double>.Zero);
        var placed = canvas.Offset;
        Frames(30);

        Assert.That(canvas.Offset, Is.EqualTo(placed));
    }
}
