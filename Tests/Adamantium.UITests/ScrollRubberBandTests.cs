using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Input.Raw;
using Adamantium.UI.Core.Media.Animation;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A scroll area pulled past its end by a hand gives way on a band and springs back; a flick runs out past
/// the end and turns back; the offset - what the scroll bars show - never leaves the ends.</summary>
[TestFixture]
public class ScrollRubberBandTests
{
    private const uint FingerId = 51;
    private const uint Step = 10;

    private Window _window;
    private ScrollContentPresenter _scroller;
    private StackPanel _rows;

    [SetUp]
    public void SetUp()
    {
        AnimationManager.Reset();
        _rows = new StackPanel();
        for (var i = 0; i < 10; i++) _rows.Children.Add(new Button { Height = 100 });

        // Ten 100-high rows in a 300-high viewport: 700 to scroll down, nothing across.
        _scroller = new ScrollContentPresenter { Content = _rows, Width = 100, Height = 300 };
        _window = new Window { Width = 100, Height = 300, Content = _scroller };
        Layout();
    }

    [TearDown]
    public void TearDown()
    {
        // The pointer device outlives the test: a finger left down would make the next test's finger a second one.
        Finger(RawPointerEventType.Up, 0, 100000);
        Mouse.Pointer.Capture(null);
        AnimationManager.Reset();
    }

    private void Layout()
    {
        for (var i = 0; i < 3; i++) WindowExtension.UpdateTree(_window);
    }

    // Where the content is shown: its top, which a stretch moves on top of the offset.
    private double ShownTop => _rows.Bounds.Y;

    private void Frames(int count)
    {
        for (var i = 0; i < count; i++)
        {
            AnimationManager.Tick(1 / 60.0);
            Layout();
        }
    }

    private void Finger(RawPointerEventType type, double y, uint at) =>
        PointerDevice.Current.ProcessEvent(new RawPointerEventArgs(type, _window, new Vector2<double>(50, y),
            InputModifiers.None, at)
        {
            PointerId = FingerId,
            PointerType = PointerType.Touch,
            IsPrimary = true
        });

    // A finger from y = 50 down by `by` in five moves - past the top, where the content stands at 0.
    private uint PullDown(double by)
    {
        uint at = 1000;
        Finger(RawPointerEventType.Down, 50, at);
        for (var i = 1; i <= 5; i++) Finger(RawPointerEventType.Move, 50 + i * by / 5, at += Step);
        Layout();
        return at;
    }

    [Test]
    public void AFingerPastTheTopStretchesTheContentLessThanItMoved_AndTheOffsetStaysAtTheEnd()
    {
        PullDown(150);

        Assert.Multiple(() =>
        {
            Assert.That(ShownTop, Is.GreaterThan(0).And.LessThan(150), "shown below its top, held back by the band");
            Assert.That(_scroller.Offset, Is.EqualTo(Vector2<double>.Zero), "the offset itself at the end");
        });
    }

    [Test]
    public void LetGoStill_ItSpringsBackToTheTop()
    {
        var at = PullDown(150);
        Finger(RawPointerEventType.Up, 200, at + 500);
        Frames(3);
        var springing = ShownTop;
        Frames(120);

        Assert.Multiple(() =>
        {
            Assert.That(springing, Is.GreaterThan(0), "on its way back");
            Assert.That(ShownTop, Is.EqualTo(0).Within(1e-9), "and at the top");
        });
    }

    [Test]
    public void AFlickAtTheEndRunsOutAndComesBackInOneMotion()
    {
        _scroller.SetOffset(new Vector2<double>(0, 700));   // at the bottom
        Layout();

        uint at = 1000;
        Finger(RawPointerEventType.Down, 250, at);
        for (var i = 1; i <= 5; i++) Finger(RawPointerEventType.Move, 250 - i * 8, at += Step);   // up, 800 px/s
        Finger(RawPointerEventType.Up, 210, at);

        var bottom = -700.0;
        var path = new System.Collections.Generic.List<double>();
        for (var i = 0; i < 60; i++)
        {
            Frames(1);
            path.Add(ShownTop);
        }

        var deepest = System.Linq.Enumerable.Min(path);
        Assert.Multiple(() =>
        {
            Assert.That(deepest, Is.LessThan(bottom - 1).And.GreaterThanOrEqualTo(bottom - 0.12 * 300 - 41),
                "out past the bottom, not far");
            Assert.That(path.IndexOf(deepest), Is.LessThan(15), "turned soon");
            Assert.That(ShownTop, Is.EqualTo(bottom).Within(1e-9), "back at the bottom");
            Assert.That(_scroller.Offset.Y, Is.EqualTo(700), "the offset never left the end");
        });
    }

    [Test]
    public void AFingerCatchingItOnTheWayBackTakesItWhereItIs()
    {
        var at = PullDown(150);
        Finger(RawPointerEventType.Up, 200, at + 500);
        Frames(3);
        var shown = ShownTop;

        Finger(RawPointerEventType.Down, 120, at + 600);
        Frames(30);
        Assert.That(ShownTop, Is.EqualTo(shown).Within(1e-9), "held where it was caught");

        Finger(RawPointerEventType.Up, 120, at + 1200);
        Frames(120);
        Assert.That(ShownTop, Is.EqualTo(0).Within(1e-9));
    }

    [Test]
    public void WithTheBandOff_TheContentStopsAtTheEnd()
    {
        _scroller.IsRubberBandEnabled = false;
        PullDown(150);

        Assert.That(ShownTop, Is.EqualTo(0));
    }

    [Test]
    public void TheMouseWithPanningModePullsItOnTheBandToo()
    {
        _scroller.PanningMode = PanningMode.VerticalOnly;
        var held = InputModifiers.LeftMouseButton;
        Mouse.Pointer.ScreenPosition = new PixelPoint(50, 50);
        _scroller.RaiseEvent(new PointerPressedEventArgs(Mouse.Pointer, MouseButtons.Left, held, 1000)
        {
            RoutedEvent = PointerEvents.PointerPressedEvent,
            Properties = PointerPointProperties.FromMouse(held)
        });
        for (var i = 1; i <= 5; i++)
        {
            Mouse.Pointer.ScreenPosition = new PixelPoint(50, 50 + i * 30);
            _scroller.RaiseEvent(new PointerEventArgs(Mouse.Pointer, held, 1000 + (uint)i * Step)
            {
                RoutedEvent = PointerEvents.PointerMovedEvent,
                Properties = PointerPointProperties.FromMouse(held)
            });
        }

        Layout();
        var stretched = ShownTop;
        _scroller.RaiseEvent(new PointerReleasedEventArgs(Mouse.Pointer, MouseButtons.Left, InputModifiers.None, 1600)
        {
            RoutedEvent = PointerEvents.PointerReleasedEvent,
            Properties = PointerPointProperties.FromMouse(InputModifiers.None)
        });
        Frames(120);

        Assert.Multiple(() =>
        {
            Assert.That(stretched, Is.GreaterThan(0).And.LessThan(150));
            Assert.That(ShownTop, Is.EqualTo(0).Within(1e-9), "sprung back");
        });
    }

    private void MousePress(double y, uint at)
    {
        var held = InputModifiers.LeftMouseButton;
        Mouse.Pointer.ScreenPosition = new PixelPoint(50, (int)y);
        _scroller.RaiseEvent(new PointerPressedEventArgs(Mouse.Pointer, MouseButtons.Left, held, at)
        {
            RoutedEvent = PointerEvents.PointerPressedEvent,
            Properties = PointerPointProperties.FromMouse(held)
        });
    }

    // A press that stopped the spring-back, its capture then taken away before it panned: the release never comes, and
    // the content must not be left past its end.
    [Test]
    public void APressWhoseCaptureIsTakenAwayBeforeItPannedStillSpringsBack()
    {
        _scroller.PanningMode = PanningMode.VerticalOnly;
        var at = PullDown(150);
        Finger(RawPointerEventType.Up, 200, at + 500);
        Frames(3);
        Assume.That(ShownTop, Is.GreaterThan(0));

        MousePress(100, at + 600);
        Mouse.Pointer.Capture(null);   // the system takes it
        Frames(120);

        Assert.That(ShownTop, Is.EqualTo(0).Within(1e-9));
    }

    // A click somewhere in the area, no pan of its own, leaves a running bounce alone.
    [Test]
    public void AClickWithoutPanningModeDoesNotCutABounceShort()
    {
        var at = PullDown(150);
        Finger(RawPointerEventType.Up, 200, at + 500);
        Frames(3);
        var before = ShownTop;

        _scroller.RaiseEvent(new PointerReleasedEventArgs(Mouse.Pointer, MouseButtons.Left, InputModifiers.None, at + 600)
        {
            RoutedEvent = PointerEvents.PointerReleasedEvent,
            Properties = PointerPointProperties.FromMouse(InputModifiers.None)
        });
        Frames(1);

        // On the spring from rest it would have barely moved; going on, it keeps its speed back.
        Assert.That(before - ShownTop, Is.GreaterThan(3));
    }

    [Test]
    public void SwitchingTheBandOffMidSpringPutsTheContentAtTheEndAndStops()
    {
        var at = PullDown(150);
        Finger(RawPointerEventType.Up, 200, at + 500);
        Frames(3);

        _scroller.IsRubberBandEnabled = false;
        Layout();
        Frames(30);

        Assert.Multiple(() =>
        {
            Assert.That(ShownTop, Is.EqualTo(0));
            Assert.That(_scroller.Offset.Y, Is.EqualTo(0), "it did not glide on into the content");
        });
    }

    private static PointerPressedEventArgs MiddleDown(uint at) =>
        new(Mouse.Pointer, MouseButtons.Middle, InputModifiers.MiddleMouseButton, at)
        {
            RoutedEvent = PointerEvents.PointerPressedEvent,
            Properties = PointerPointProperties.FromMouse(InputModifiers.MiddleMouseButton)
        };

    private void MiddleDrag(int from, int by, bool release = true)
    {
        uint at = 1000;
        Mouse.Pointer.ScreenPosition = new PixelPoint(50, from);
        var down = MiddleDown(at);
        _scroller.RaiseEvent(down);
        for (var i = 1; i <= 5; i++)
        {
            Mouse.Pointer.ScreenPosition = new PixelPoint(50, from + i * by / 5);
            _scroller.RaiseEvent(new PointerEventArgs(Mouse.Pointer, InputModifiers.MiddleMouseButton, at += Step)
            {
                RoutedEvent = PointerEvents.PointerMovedEvent,
                Properties = PointerPointProperties.FromMouse(InputModifiers.MiddleMouseButton)
            });
        }

        Layout();
        if (!release) return;

        _scroller.RaiseEvent(new PointerReleasedEventArgs(Mouse.Pointer, MouseButtons.Middle, InputModifiers.None, at + 500)
        {
            RoutedEvent = PointerEvents.PointerReleasedEvent,
            Properties = PointerPointProperties.FromMouse(InputModifiers.None)
        });
    }

    [Test]
    public void TheMiddleButtonDragsTheContentWithoutPanningMode()
    {
        MiddleDrag(250, -100);   // up: the content follows, scrolled down by as much
        Assert.That(_scroller.Offset.Y, Is.EqualTo(100).Within(1e-9));
    }

    [Test]
    public void TheMiddleButtonPullsItPastTheEndOnTheBand()
    {
        MiddleDrag(50, 150, release: false);
        var stretched = ShownTop;

        _scroller.RaiseEvent(new PointerReleasedEventArgs(Mouse.Pointer, MouseButtons.Middle, InputModifiers.None, 2000)
        {
            RoutedEvent = PointerEvents.PointerReleasedEvent,
            Properties = PointerPointProperties.FromMouse(InputModifiers.None)
        });
        Frames(120);

        Assert.Multiple(() =>
        {
            Assert.That(stretched, Is.GreaterThan(0).And.LessThan(150), "past the top on the band");
            Assert.That(ShownTop, Is.EqualTo(0).Within(1e-9), "sprung back");
        });
    }

    [Test]
    public void TheMiddleButtonSwitchedOff_DragsNothing()
    {
        _scroller.IsMiddleButtonPanEnabled = false;
        MiddleDrag(250, -100);
        Assert.That(_scroller.Offset.Y, Is.EqualTo(0));
    }

    [Test]
    public void AnAreaWithNowhereToGoLeavesTheMiddleButtonToTheOneAroundIt()
    {
        var inner = new ScrollContentPresenter { Content = new Button { Height = 50 }, Height = 100 };
        var rows = new StackPanel();
        rows.Children.Add(inner);
        for (var i = 0; i < 9; i++) rows.Children.Add(new Button { Height = 100 });
        _scroller.Content = rows;
        Layout();

        Mouse.Pointer.ScreenPosition = new PixelPoint(50, 50);
        inner.RaiseEvent(MiddleDown(1000));
        Mouse.Pointer.ScreenPosition = new PixelPoint(50, 20);
        inner.RaiseEvent(new PointerEventArgs(Mouse.Pointer, InputModifiers.MiddleMouseButton, 1010)
        {
            RoutedEvent = PointerEvents.PointerMovedEvent,
            Properties = PointerPointProperties.FromMouse(InputModifiers.MiddleMouseButton)
        });

        Assert.That(ReferenceEquals(Mouse.Pointer.Captured, _scroller), Is.True, "the outer area took it");
    }

    // THE REAL PATH: the mouse through the pointer device - hit test, capture, routing - as the window feeds it.
    private void Mouse3(RawPointerEventType type, double x, double y, uint at, MouseButtons button = MouseButtons.Middle)
    {
        var held = type == RawPointerEventType.Up ? InputModifiers.None : InputModifiers.MiddleMouseButton;
        PointerDevice.Current.ProcessEvent(new RawPointerEventArgs(type, _window, new Vector2<double>(x, y), held, at)
        {
            ChangedButton = type is RawPointerEventType.Down or RawPointerEventType.Up ? button : MouseButtons.None,
            Properties = PointerPointProperties.FromMouse(held)
        });
    }

    private void MiddleDragThrough(double x, double y, double dx, double dy, uint at = 1000)
    {
        Mouse3(RawPointerEventType.Down, x, y, at);
        for (var i = 1; i <= 5; i++) Mouse3(RawPointerEventType.Move, x + i * dx / 5, y + i * dy / 5, at += Step);
        Mouse3(RawPointerEventType.Up, x + dx, y + dy, at + 500);
        Layout();
    }

    [Test]
    public void ThroughTheDevice_AMiddleDragScrollsAndAMiddleClickReachesTheWindowUnhandled()
    {
        var unhandled = 0;
        _window.AddHandler(PointerEvents.PointerPressedEvent, new PointerPressedEventHandler((_, _) => unhandled++));

        Mouse3(RawPointerEventType.Down, 50, 150, 1000);
        Mouse3(RawPointerEventType.Up, 50, 150, 1100);
        Assert.That(unhandled, Is.EqualTo(1), "a click is nobody's pan - it goes on up");

        MiddleDragThrough(50, 250, 0, -100, 2000);
        Assert.That(_scroller.Offset.Y, Is.EqualTo(100).Within(1e-9), "a drag scrolls");
    }

    [Test]
    public void ThroughTheDevice_TheNearestAreaThatCanMoveTheWayTheDragGoesTakesIt()
    {
        // An inner column scrolling down only, inside an outer strip scrolling across only.
        var column = new StackPanel();
        for (var i = 0; i < 10; i++) column.Children.Add(new Button { Height = 100 });
        var inner = new ScrollContentPresenter { Content = column, Width = 100, Height = 300, CanScrollHorizontally = false };
        var strip = new StackPanel { Orientation = Orientation.Horizontal };
        strip.Children.Add(inner);
        strip.Children.Add(new Button { Width = 400, Height = 300 });
        _scroller = new ScrollContentPresenter { Content = strip, Width = 100, Height = 300, CanScrollVertically = false };
        _window.Content = _scroller;
        Layout();

        MiddleDragThrough(50, 150, -100, 0);
        Assert.Multiple(() =>
        {
            Assert.That(_scroller.Offset.X, Is.EqualTo(100).Within(1e-9), "sideways: the outer strip");
            Assert.That(inner.Offset.Y, Is.EqualTo(0), "the column left alone");
        });

        _scroller.SetOffset(Vector2<double>.Zero);
        Layout();
        MiddleDragThrough(50, 250, 0, -100, 5000);
        Assert.Multiple(() =>
        {
            Assert.That(inner.Offset.Y, Is.EqualTo(100).Within(1e-9), "down: the column");
            Assert.That(_scroller.Offset.X, Is.EqualTo(0), "the strip left alone");
        });
    }

    [Test]
    public void ThroughTheDevice_WhatTakesTheMiddleButtonItselfKeepsIt()
    {
        var turner = new Turner { Height = 100 };
        _rows.Children.Insert(0, turner);
        Layout();

        MiddleDragThrough(50, 50, 0, 40);

        Assert.Multiple(() =>
        {
            Assert.That(turner.Moves, Is.GreaterThan(0), "the view kept its drag");
            Assert.That(_scroller.Offset.Y, Is.EqualTo(0));
            Assert.That(ShownTop, Is.EqualTo(0), "and nothing stretched");
        });
    }

    // Takes the middle button for itself - captured, not handled - as a 3D view does.
    private sealed class Turner : Border
    {
        public int Moves { get; private set; }

        public Turner()
        {
            Background = Adamantium.UI.Core.Media.Brushes.Gray;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (e.ChangedButton == MouseButtons.Middle) e.Pointer.Capture(this);
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (ReferenceEquals(e.Pointer.Captured, this)) Moves++;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (ReferenceEquals(e.Pointer.Captured, this)) e.Pointer.Capture(null);
        }
    }

    [Test]
    public void SettingTheOffsetLetsGoOfAStretch()
    {
        PullDown(150);
        _scroller.SetOffset(new Vector2<double>(0, 200));
        Layout();

        Assert.That(ShownTop, Is.EqualTo(-200));
    }
}
