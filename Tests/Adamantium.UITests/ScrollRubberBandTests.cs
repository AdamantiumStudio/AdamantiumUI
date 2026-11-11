using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Buttons;
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

    [Test]
    public void SettingTheOffsetLetsGoOfAStretch()
    {
        PullDown(150);
        _scroller.SetOffset(new Vector2<double>(0, 200));
        Layout();

        Assert.That(ShownTop, Is.EqualTo(-200));
    }
}
