using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Input.Raw;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>The device's model of pointers, fed the raw events a platform sends: every pointer is its own - its capture,
/// the element under it and its click count - so two fingers work two things at once.</summary>
[TestFixture]
public class PointerDeviceTests
{
    private Window _window;
    private Border _left;
    private Border _right;

    [SetUp]
    public void SetUp()
    {
        _left = new Border { Width = 100, Height = 100, Background = Brushes.Gray };
        _right = new Border { Width = 100, Height = 100, Background = Brushes.Gray };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_left);
        row.Children.Add(_right);
        _window = new Window { Width = 200, Height = 100, Content = row };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(_window);
        }

        // Whatever pressed a pointer down presses it into its own capture, as a drag would.
        foreach (var border in new[] { _left, _right })
        {
            border.AddHandler(PointerEvents.PointerPressedEvent,
                new PointerPressedEventHandler((sender, e) => e.Pointer.Capture((IInputComponent)sender)), handledEventsToo: true);
        }
    }

    [TearDown]
    public void TearDown()
    {
        Mouse.Pointer.Capture(null);
        Send(RawPointerEventType.LeaveWindow, PointerDevice.MouseId, PointerType.Mouse, Vector2.Zero, 0);
    }

    [Test]
    public void TwoFingers_EachHoldTheirOwnElement()
    {
        Send(RawPointerEventType.Down, 10, PointerType.Touch, new Vector2(50, 50), 0);
        Send(RawPointerEventType.Down, 11, PointerType.Touch, new Vector2(150, 50), 0);

        var first = Finger(10);
        var second = Finger(11);
        Assert.Multiple(() =>
        {
            Assert.That(first.Captured, Is.SameAs(_left));
            Assert.That(second.Captured, Is.SameAs(_right));
            Assert.That(first.DirectlyOver, Is.SameAs(_left));
            Assert.That(second.DirectlyOver, Is.SameAs(_right));
            Assert.That(_left.IsPointerOver && _right.IsPointerOver, Is.True, "each finger lights what it is on");
        });

        // The first finger slides over the second one's element and stays with its own.
        var movedOnLeft = 0;
        _left.PointerMoved += (_, _) => movedOnLeft++;
        Send(RawPointerEventType.Move, 10, PointerType.Touch, new Vector2(150, 50), 0);

        Assert.Multiple(() =>
        {
            Assert.That(movedOnLeft, Is.EqualTo(1), "the move goes to the element the finger holds");
            Assert.That(first.Captured, Is.SameAs(_left));
            Assert.That(second.Captured, Is.SameAs(_right), "and the other finger's hold is untouched");
        });
    }

    [Test]
    public void UnderACapture_ThePointerIsOverWhatIsUnderIt_InAnOffsetPanel()
    {
        var first = new Border { Width = 100, Height = 100, Background = Brushes.Gray };
        var second = new Border { Width = 100, Height = 100, Background = Brushes.Gray };
        var held = new StackPanel { Orientation = Orientation.Horizontal };
        held.Children.Add(first);
        held.Children.Add(second);
        var offset = new StackPanel { Margin = new Thickness(40) };
        offset.Children.Add(held);
        _window.Content = offset;
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(_window);
        }

        held.AddHandler(PointerEvents.PointerPressedEvent,
            new PointerPressedEventHandler((_, e) => e.Pointer.Capture(held)), handledEventsToo: true);

        Send(RawPointerEventType.Down, PointerDevice.MouseId, PointerType.Mouse, new Vector2(60, 60), 0);
        Send(RawPointerEventType.Move, PointerDevice.MouseId, PointerType.Mouse, new Vector2(130, 60), 0);

        Assert.That(Mouse.Pointer.Captured, Is.SameAs(held));
        Assert.That(Mouse.Pointer.DirectlyOver, Is.SameAs(first), "90 into the panel is still the first of its two");
    }

    [Test]
    public void ALiftedFinger_IsGoneAndLetsGo()
    {
        Send(RawPointerEventType.Down, 10, PointerType.Touch, new Vector2(50, 50), 0);
        Send(RawPointerEventType.Down, 11, PointerType.Touch, new Vector2(150, 50), 0);
        var first = Finger(10);

        Send(RawPointerEventType.Up, 10, PointerType.Touch, new Vector2(50, 50), 0);

        Assert.Multiple(() =>
        {
            Assert.That(PointerDevice.Current.Pointers.Any(p => p.Id == 10), Is.False);
            Assert.That(first.Captured, Is.Null);
            Assert.That(_left.IsPointerOver, Is.False, "nothing is touching the left one any more");
            Assert.That(Finger(11).Captured, Is.SameAs(_right), "the other finger still holds its own");
        });

        Send(RawPointerEventType.Cancel, 11, PointerType.Touch, new Vector2(150, 50), 0);

        Assert.Multiple(() =>
        {
            Assert.That(PointerDevice.Current.Pointers.Any(p => p.Type == PointerType.Touch), Is.False);
            Assert.That(_right.IsPointerOver, Is.False);
        });
    }

    // A tap elsewhere in between does not break a double click: the count belongs to the pointer that clicked.
    [Test]
    public void ClickCount_IsCountedPerPointer()
    {
        var mouseClicks = 0;
        _left.AddHandler(PointerEvents.PointerPressedEvent, new PointerPressedEventHandler((_, e) =>
        {
            if (e.Pointer.Type == PointerType.Mouse) mouseClicks = e.ClickCount;
        }), handledEventsToo: true);

        Send(RawPointerEventType.Down, PointerDevice.MouseId, PointerType.Mouse, new Vector2(50, 50), 1000);
        Send(RawPointerEventType.Up, PointerDevice.MouseId, PointerType.Mouse, new Vector2(50, 50), 1010);
        Send(RawPointerEventType.Down, 20, PointerType.Touch, new Vector2(150, 50), 1020);
        Send(RawPointerEventType.Up, 20, PointerType.Touch, new Vector2(150, 50), 1030);
        Send(RawPointerEventType.Down, PointerDevice.MouseId, PointerType.Mouse, new Vector2(50, 50), 1040);
        Send(RawPointerEventType.Up, PointerDevice.MouseId, PointerType.Mouse, new Vector2(50, 50), 1050);

        Assert.That(mouseClicks, Is.EqualTo(2));
    }

    // Controls answer the left button. A finger presses it whatever its source said - here, nothing at all - or a
    // platform that forgot would leave every button deaf to touch.
    [Test]
    public void AFinger_ClicksAButton_WhateverItsSourceSaid()
    {
        var button = new Adamantium.UI.Controls.Buttons.Button { Width = 100, Height = 100 };
        _window.Content = button;
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(_window);
        }

        var clicks = 0;
        button.Click += (_, _) => clicks++;

        Send(RawPointerEventType.Down, 30, PointerType.Touch, new Vector2(50, 50), 0, MouseButtons.None);
        Send(RawPointerEventType.Up, 30, PointerType.Touch, new Vector2(50, 50), 0, MouseButtons.Right);

        Assert.That(clicks, Is.EqualTo(1));
    }

    [Test]
    public void AFinger_HoldsTheLeftButtonWhileItLies()
    {
        bool? held = null;
        _left.PointerMoved += (_, e) => held = e.Properties.IsLeftButtonPressed;

        Send(RawPointerEventType.Down, 31, PointerType.Touch, new Vector2(50, 50), 0, MouseButtons.None);
        Send(RawPointerEventType.Move, 31, PointerType.Touch, new Vector2(60, 50), 0, MouseButtons.None);

        Assert.That(held, Is.True);
        Send(RawPointerEventType.Up, 31, PointerType.Touch, new Vector2(60, 50), 0, MouseButtons.None);
    }

    // A pen's barrel held as its tip touches is the right button - the pen's way of asking for a context menu.
    [Test]
    public void APensBarrel_PressesTheRightButton()
    {
        var pressed = MouseButtons.None;
        _left.AddHandler(PointerEvents.PointerPressedEvent,
            new PointerPressedEventHandler((_, e) => pressed = e.ChangedButton), handledEventsToo: true);

        var barrel = new PointerPointProperties { IsBarrelButtonPressed = true };
        Send(RawPointerEventType.Down, 32, PointerType.Pen, new Vector2(50, 50), 0, MouseButtons.Left, barrel);
        Send(RawPointerEventType.Up, 32, PointerType.Pen, new Vector2(50, 50), 0, MouseButtons.Left, barrel);

        Assert.That(pressed, Is.EqualTo(MouseButtons.Right));
    }

    // The side button pressed while the tip already touches changes nothing: the pen lifts the button it pressed, so the
    // element that took the press is let go of.
    [Test]
    public void APensSideButtonPressedMidStroke_LiftsTheButtonItPressed()
    {
        var pressed = MouseButtons.None;
        var released = MouseButtons.None;
        var moveHeldLeft = false;
        _left.AddHandler(PointerEvents.PointerPressedEvent,
            new PointerPressedEventHandler((_, e) => pressed = e.ChangedButton), handledEventsToo: true);
        _left.AddHandler(PointerEvents.PointerReleasedEvent,
            new PointerReleasedEventHandler((_, e) => released = e.ChangedButton), handledEventsToo: true);
        _left.PointerMoved += (_, e) => moveHeldLeft = e.Properties.IsLeftButtonPressed;

        var barrel = new PointerPointProperties { IsBarrelButtonPressed = true };
        Send(RawPointerEventType.Down, 33, PointerType.Pen, new Vector2(50, 50), 0, MouseButtons.None);
        Send(RawPointerEventType.Move, 33, PointerType.Pen, new Vector2(55, 50), 0, MouseButtons.None, barrel);
        Send(RawPointerEventType.Up, 33, PointerType.Pen, new Vector2(55, 50), 0, MouseButtons.None, barrel);
        Send(RawPointerEventType.LeaveWindow, 33, PointerType.Pen, Vector2.Zero, 0, MouseButtons.None);

        Assert.Multiple(() =>
        {
            Assert.That(pressed, Is.EqualTo(MouseButtons.Left));
            Assert.That(moveHeldLeft, Is.True, "the stroke still holds the button it pressed");
            Assert.That(released, Is.EqualTo(MouseButtons.Left));
        });
    }

    // A finger hovering over a digitizer that sees it is no press: only a touch holds the left button.
    [Test]
    public void AHoveringFinger_HoldsNoButton()
    {
        bool? held = null;
        _left.PointerMoved += (_, e) => held = e.Properties.IsLeftButtonPressed;

        Send(RawPointerEventType.Move, 34, PointerType.Touch, new Vector2(50, 50), 0, MouseButtons.None);

        Assert.That(held, Is.False);
        Send(RawPointerEventType.LeaveWindow, 34, PointerType.Touch, Vector2.Zero, 0, MouseButtons.None);
    }

    // A finger held still on a button holds: the hold starts, the button lets go of the press, and the lift completes
    // the hold instead of clicking.
    [Test]
    public void AFingerHeldOnAButton_HoldsInsteadOfClicking()
    {
        var button = HostedButton();
        var clicks = 0;
        var states = new List<HoldingState>();
        button.Click += (_, _) => clicks++;
        button.AddHandler(Gestures.HoldingEvent, new HoldingEventHandler((_, e) => states.Add(e.HoldingState)));

        using (HoldingTimeOf(TimeSpan.FromHours(1)))
        {
            Send(RawPointerEventType.Down, 40, PointerType.Touch, new Vector2(50, 50), 0, MouseButtons.None);
            PointerDevice.Current.Holding.Elapsed(40, 600);
            Assert.That(button.IsPressed, Is.False, "the held button let go of the press");
            Send(RawPointerEventType.Up, 40, PointerType.Touch, new Vector2(50, 50), 700, MouseButtons.None);
        }

        Assert.Multiple(() =>
        {
            Assert.That(states, Is.EqualTo(new[] { HoldingState.Started, HoldingState.Completed }));
            Assert.That(clicks, Is.Zero);
        });
    }

    private static void Hold(IInputComponent target, Pointer pointer, HoldingState state) =>
        target.RaiseEvent(new HoldingEventArgs(pointer, state, InputModifiers.None, 0)
        {
            RoutedEvent = Gestures.HoldingEvent,
        });

    private static int HoldCues(Window window) => window.Adorners.OfType<Adamantium.UI.Controls.HoldIndicator>().Count();

    // A cue is kept with its contact: two contacts numbered alike - as test windows side by side number theirs - each
    // show one in their own window, and the lift of one takes off only its own.
    [Test]
    public void TheCuesOfTwoContactsNumberedAlike_AreEachTheirOwn()
    {
        var other = new Border { Width = 100, Height = 100, Background = Brushes.Gray };
        var otherWindow = new Window { Width = 100, Height = 100, Content = other };
        WindowExtension.UpdateTree(otherWindow);
        var here = new Pointer(7, PointerType.Touch, true);
        var there = new Pointer(7, PointerType.Touch, true);
        try
        {
            Hold(_left, here, HoldingState.Started);
            Hold(other, there, HoldingState.Started);
            var both = (HoldCues(_window), HoldCues(otherWindow));
            Hold(_left, here, HoldingState.Completed);

            Assert.Multiple(() =>
            {
                Assert.That(both, Is.EqualTo((1, 1)));
                Assert.That(HoldCues(_window), Is.Zero);
                Assert.That(HoldCues(otherWindow), Is.EqualTo(1));
            });
        }
        finally
        {
            Hold(other, there, HoldingState.Canceled);
            otherWindow.Close();
        }
    }

    // An element taken out of the window while held - a row recycled, a template swapped - still takes the cue down
    // with the lift: the cue comes off the window it went up in.
    [Test]
    public void TheCueOfAHeldElementTakenAway_ComesDownWithTheLift()
    {
        var pointer = new Pointer(8, PointerType.Touch, true);
        Hold(_left, pointer, HoldingState.Started);
        var shown = HoldCues(_window);
        ((StackPanel)_window.Content).Children.Remove(_left);
        Hold(_left, pointer, HoldingState.Completed);

        Assert.That(shown, Is.EqualTo(1));
        Assert.That(HoldCues(_window), Is.Zero);
    }

    // The cue a hold wears is shown once: the lift takes it off the window and throws it away. Only taken off, it stayed
    // an inheritance child of the window, every hold of the session adding one.
    [Test]
    public void TheCueOfAHold_IsDiscardedOnTheLift()
    {
        HostedButton();
        Adamantium.UI.Controls.HoldIndicator cue;
        using (HoldingTimeOf(TimeSpan.FromHours(1)))
        {
            Send(RawPointerEventType.Down, 43, PointerType.Touch, new Vector2(50, 50), 0, MouseButtons.None);
            PointerDevice.Current.Holding.Elapsed(43, 600);
            cue = _window.Adorners.OfType<Adamantium.UI.Controls.HoldIndicator>().SingleOrDefault();
            Send(RawPointerEventType.Up, 43, PointerType.Touch, new Vector2(50, 50), 700, MouseButtons.None);
        }

        Assume.That(cue, Is.Not.Null, "the hold showed its cue");
        while (DiscardedVisuals.Drain(64) > 0) { }

        Assert.Multiple(() =>
        {
            Assert.That(_window.Adorners, Has.None.InstanceOf<Adamantium.UI.Controls.HoldIndicator>(), "off the window");
            Assert.That(cue.IsDiscarded, Is.True, "thrown away");
        });
    }

    [Test]
    public void AFingerThatMovesOff_IsNoHold()
    {
        var states = new List<HoldingState>();
        _left.AddHandler(Gestures.HoldingEvent, new HoldingEventHandler((_, e) => states.Add(e.HoldingState)));

        using (HoldingTimeOf(TimeSpan.FromHours(1)))
        {
            Send(RawPointerEventType.Down, 41, PointerType.Touch, new Vector2(20, 50), 0, MouseButtons.None);
            Send(RawPointerEventType.Move, 41, PointerType.Touch, new Vector2(60, 50), 0, MouseButtons.None);
            PointerDevice.Current.Holding.Elapsed(41, 600);
            Send(RawPointerEventType.Up, 41, PointerType.Touch, new Vector2(60, 50), 700, MouseButtons.None);
        }

        Assert.That(states, Is.Empty);
    }

    // A started hold that then moves off is canceled, so what waits for its completion - a context menu - does not come.
    [Test]
    public void AStartedHoldThatMovesOff_IsCanceled()
    {
        var states = new List<HoldingState>();
        _left.AddHandler(Gestures.HoldingEvent, new HoldingEventHandler((_, e) => states.Add(e.HoldingState)));

        using (HoldingTimeOf(TimeSpan.FromHours(1)))
        {
            Send(RawPointerEventType.Down, 42, PointerType.Pen, new Vector2(20, 50), 0, MouseButtons.None);
            PointerDevice.Current.Holding.Elapsed(42, 600);
            Send(RawPointerEventType.Move, 42, PointerType.Pen, new Vector2(60, 50), 650, MouseButtons.None);
            Send(RawPointerEventType.Up, 42, PointerType.Pen, new Vector2(60, 50), 700, MouseButtons.None);
            Send(RawPointerEventType.LeaveWindow, 42, PointerType.Pen, Vector2.Zero, 700, MouseButtons.None);
        }

        Assert.That(states, Is.EqualTo(new[] { HoldingState.Started, HoldingState.Canceled }));
    }

    // With no loop running, the hold's timer ticks on the pool: the hold waits for whoever drains the loop's queue and
    // is raised there, never on the timer's thread beside the one changing the tree.
    [Test]
    public void AHoldWithNoLoopRunning_IsRaisedByWhoeverDrains_NotOnTheTimersThread()
    {
        var states = new List<HoldingState>();
        var threads = new List<System.Threading.Thread>();
        _left.AddHandler(Gestures.HoldingEvent, new HoldingEventHandler((_, e) =>
        {
            states.Add(e.HoldingState);
            threads.Add(System.Threading.Thread.CurrentThread);
        }));

        using (HoldingTimeOf(TimeSpan.FromMilliseconds(5)))
        {
            Send(RawPointerEventType.Down, 44, PointerType.Touch, new Vector2(20, 50), 0, MouseButtons.None);
            System.Threading.Thread.Sleep(200);
            var beforeDrain = states.Count;
            LoopSignal.Drain();
            Send(RawPointerEventType.Up, 44, PointerType.Touch, new Vector2(20, 50), 700, MouseButtons.None);

            Assert.Multiple(() =>
            {
                Assert.That(beforeDrain, Is.Zero, "nothing raised on the timer's thread");
                Assert.That(states, Is.EqualTo(new[] { HoldingState.Started, HoldingState.Completed }));
                Assert.That(threads, Has.All.SameAs(System.Threading.Thread.CurrentThread));
            });
        }
    }

    [Test]
    public void ATickLeftFromALiftedContact_DoesNotStartTheNextHoldOfItsId()
    {
        var states = new List<HoldingState>();
        _left.AddHandler(Gestures.HoldingEvent, new HoldingEventHandler((_, e) => states.Add(e.HoldingState)));

        using (HoldingTimeOf(TimeSpan.FromMilliseconds(5)))
        {
            Send(RawPointerEventType.Down, 45, PointerType.Touch, new Vector2(20, 50), 0, MouseButtons.None);
            System.Threading.Thread.Sleep(200);
            Send(RawPointerEventType.Up, 45, PointerType.Touch, new Vector2(20, 50), 300, MouseButtons.None);
        }

        using (HoldingTimeOf(TimeSpan.FromHours(1)))
        {
            Send(RawPointerEventType.Down, 45, PointerType.Touch, new Vector2(20, 50), 400, MouseButtons.None);
            LoopSignal.Drain();
            Send(RawPointerEventType.Up, 45, PointerType.Touch, new Vector2(20, 50), 500, MouseButtons.None);
        }

        Assert.That(states, Is.Empty);
    }

    [Test]
    public void TheMouse_NeverHolds()
    {
        var states = new List<HoldingState>();
        _left.AddHandler(Gestures.HoldingEvent, new HoldingEventHandler((_, e) => states.Add(e.HoldingState)));

        Send(RawPointerEventType.Down, PointerDevice.MouseId, PointerType.Mouse, new Vector2(50, 50), 0);
        PointerDevice.Current.Holding.Elapsed(PointerDevice.MouseId, 600);
        Send(RawPointerEventType.Up, PointerDevice.MouseId, PointerType.Mouse, new Vector2(50, 50), 700);

        Assert.That(states, Is.Empty);
    }

    private Adamantium.UI.Controls.Buttons.Button HostedButton()
    {
        var button = new Adamantium.UI.Controls.Buttons.Button { Width = 100, Height = 100 };
        _window.Content = button;
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(_window);
        }

        return button;
    }

    // The hold time for a test that says itself when the contact has rested: a real timer must not race it.
    private static IDisposable HoldingTimeOf(TimeSpan time)
    {
        var was = Gestures.HoldingTime;
        Gestures.HoldingTime = time;
        return new Restore(() => Gestures.HoldingTime = was);
    }

    private sealed class Restore(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }

    private static Pointer Finger(uint id) => PointerDevice.Current.Pointers.Single(p => p.Id == id);

    // The left button for a press or a release, as a platform says it for the mouse.
    private void Send(RawPointerEventType type, uint id, PointerType pointerType, Vector2 at, uint timestamp) =>
        Send(type, id, pointerType, at, timestamp,
            type is RawPointerEventType.Down or RawPointerEventType.Up ? MouseButtons.Left : MouseButtons.None);

    private void Send(RawPointerEventType type, uint id, PointerType pointerType, Vector2 at, uint timestamp,
        MouseButtons button, PointerPointProperties properties = default)
    {
        PointerDevice.Current.ProcessEvent(new RawPointerEventArgs(type, _window, at, InputModifiers.None, timestamp)
        {
            PointerId = id,
            PointerType = pointerType,
            IsPrimary = pointerType == PointerType.Mouse,
            ChangedButton = button,
            Properties = properties
        });
    }
}
