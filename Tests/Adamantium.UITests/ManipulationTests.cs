using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Input.Raw;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Animation;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>Gestures of two to ten fingers on an element that takes them: what each is recognized as, how far it
/// went, what the fingers pressed before it started, and fingers joining and leaving on the way. The fingers stand on a
/// circle about the middle of a 1000 x 600 pad and move a step of every finger at a time, as a digitizer's frames come.
/// </summary>
[TestFixture]
public class ManipulationTests
{
    private const uint FirstId = 80;
    private const double Pixel = 1;
    private static readonly Vector2 Middle = new(500, 300);

    private Window _window;
    private Border _pad;
    private Button _button;
    private List<ManipulationEventArgs> _events;
    private TimeSpan _holdingTime;

    [SetUp]
    public void SetUp()
    {
        _holdingTime = Gestures.HoldingTime;
        Gestures.HoldingTime = TimeSpan.FromHours(1);

        _button = new Button
        {
            Width = 100, Height = 100, HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(350, 250, 0, 0)
        };
        var inside = new Grid();
        inside.Children.Add(_button);
        _pad = new Border { Background = Brushes.Gray, Child = inside };
        Gestures.SetIsManipulationEnabled(_pad, true);

        _events = [];
        _pad.AddHandler(Gestures.ManipulationEvent, new ManipulationEventHandler((_, e) => _events.Add(e)));
        _window = new Window { Width = 1000, Height = 600, Content = _pad };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(_window);
        }
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var left in PointerDevice.Current.Pointers.Where(p => p.Id >= FirstId && p.Id < FirstId + 10).ToList())
        {
            Send(RawPointerEventType.Cancel, (int)(left.Id - FirstId), Vector2.Zero);
        }

        Gestures.HoldingTime = _holdingTime;
        AnimationManager.Reset();
    }

    [Test]
    public void FingersSpreading_PinchAboutTheirMiddle([Range(2, 10)] int fingers)
    {
        Down(fingers, radius: 100);

        // 10 a step: the first step spreads them less than the threshold, so the pinch counts from the second.
        for (var radius = 110; radius <= 200; radius += 10)
        {
            MoveAll(fingers, radius);
        }

        Up(fingers, 200);

        var deltas = Of(ManipulationState.Delta);
        Assert.Multiple(() =>
        {
            Assert.That(Of(ManipulationState.Started), Has.Count.EqualTo(1));
            Assert.That(Of(ManipulationState.Completed), Has.Count.EqualTo(1));
            Assert.That(deltas[^1].Recognized, Is.EqualTo(ManipulationKinds.Pinch), "the middle stayed and they did not turn");
            Assert.That(deltas[^1].TotalScale, Is.EqualTo(200.0 / 110).Within(0.02));
            Assert.That(deltas[^1].TotalTranslation.Length(), Is.LessThan(1e-6));
            Assert.That(deltas[^1].Origin.X, Is.EqualTo(Middle.X).Within(Pixel));
            Assert.That(deltas[^1].Origin.Y, Is.EqualTo(Middle.Y).Within(Pixel));
            Assert.That(deltas[^1].Contacts, Is.EqualTo(fingers));
        });
    }

    [Test]
    public void FingersMovingTogether_Pan([Values(2, 5, 10)] int fingers)
    {
        Down(fingers, radius: 100);
        for (var step = 1; step <= 6; step++)
        {
            MoveAll(fingers, 100, offset: new Vector2(10 * step, 5 * step));
        }

        Up(fingers, 100, new Vector2(60, 30));

        var last = Of(ManipulationState.Delta)[^1];
        Assert.Multiple(() =>
        {
            Assert.That(last.Recognized, Is.EqualTo(ManipulationKinds.Pan));
            Assert.That(last.TotalTranslation.X, Is.EqualTo(60).Within(Pixel));
            Assert.That(last.TotalTranslation.Y, Is.EqualTo(30).Within(Pixel));
            Assert.That(last.TotalScale, Is.EqualTo(1));
        });
    }

    [Test]
    public void FingersTurning_Rotate([Values(2, 5, 10)] int fingers)
    {
        Down(fingers, radius: 100);

        // 5 degrees a step: past the threshold of 8 on the second.
        for (var step = 1; step <= 6; step++)
        {
            MoveAll(fingers, 100, turn: 5 * step);
        }

        Up(fingers, 100, turn: 30);

        var last = Of(ManipulationState.Delta)[^1];
        Assert.Multiple(() =>
        {
            Assert.That(last.Recognized, Is.EqualTo(ManipulationKinds.Rotate));
            Assert.That(last.TotalRotation, Is.EqualTo(25).Within(1.0));
            Assert.That(last.TotalScale, Is.EqualTo(1).Within(1e-9));
        });
    }

    [Test]
    public void OneFinger_IsNoGesture_AndClicksTheButton()
    {
        var clicks = 0;
        _button.Click += (_, _) => clicks++;

        Send(RawPointerEventType.Down, 0, new Vector2(400, 300));
        Send(RawPointerEventType.Move, 0, new Vector2(402, 301));
        Send(RawPointerEventType.Up, 0, new Vector2(402, 301));

        Assert.Multiple(() =>
        {
            Assert.That(_events, Is.Empty);
            Assert.That(clicks, Is.EqualTo(1));
        });
    }

    // The first finger pressed the button; once the second makes a gesture of them, the button lets go and is not
    // clicked as the fingers lift.
    [Test]
    public void TheGesture_TakesTheFingersFromWhatTheyPressed([Range(2, 10)] int fingers)
    {
        var clicks = 0;
        _button.Click += (_, _) => clicks++;
        Send(RawPointerEventType.Down, 0, new Vector2(400, 300));
        for (var i = 1; i < fingers; i++)
        {
            Send(RawPointerEventType.Down, i, new Vector2(600 + 30 * i, 300));
        }

        Assert.That(_button.IsPressed, Is.True, "still a press until a gesture is recognized");

        for (var step = 1; step <= 3; step++)
        {
            Send(RawPointerEventType.Move, 0, new Vector2(400 - 10 * step, 300));
            for (var i = 1; i < fingers; i++)
            {
                Send(RawPointerEventType.Move, i, new Vector2(600 + 30 * i + 10 * step, 300));
            }
        }

        var pressed = _button.IsPressed;
        Send(RawPointerEventType.Up, 0, new Vector2(370, 300));
        for (var i = 1; i < fingers; i++)
        {
            Send(RawPointerEventType.Up, i, new Vector2(630 + 30 * i, 300));
        }

        Assert.Multiple(() =>
        {
            Assert.That(Of(ManipulationState.Started), Has.Count.EqualTo(1));
            Assert.That(pressed, Is.False, "the gesture took the finger");
            Assert.That(clicks, Is.Zero);
        });
    }

    // A finger landing on a gesture under way joins it: its press reaches nothing, and the middle moving to take it in
    // is no pan.
    [Test]
    public void AFingerJoining_ReachesNothing_AndMovesNothing()
    {
        var pressed = 0;
        _button.AddHandler(PointerEvents.PointerPressedEvent, new PointerPressedEventHandler((_, _) => pressed++),
            handledEventsToo: true);
        Down(2, radius: 100);
        for (var step = 1; step <= 3; step++)
        {
            MoveAll(2, 100, offset: new Vector2(10 * step, 0));
        }

        pressed = 0;
        Send(RawPointerEventType.Down, 2, new Vector2(400, 300));
        for (var step = 4; step <= 6; step++)
        {
            MoveAll(2, 100, offset: new Vector2(10 * step, 0));
            Send(RawPointerEventType.Move, 2, new Vector2(400 + 10 * (step - 3), 300));
        }

        var last = Of(ManipulationState.Delta)[^1];
        Up(2, 100, new Vector2(60, 0));
        Send(RawPointerEventType.Up, 2, new Vector2(430, 300));
        Assert.Multiple(() =>
        {
            Assert.That(pressed, Is.Zero, "the third finger pressed nothing");
            Assert.That(last.Contacts, Is.EqualTo(3));
            Assert.That(last.TotalTranslation.X, Is.EqualTo(60).Within(Pixel), "the join added no jump");
            Assert.That(last.TotalTranslation.Y, Is.EqualTo(0).Within(Pixel));
        });
    }

    [Test]
    public void TheGesture_GoesOnWithOneFinger_AndCompletesWithTheLast()
    {
        Down(2, radius: 100);
        MoveAll(2, 100, offset: new Vector2(10, 0));
        Send(RawPointerEventType.Up, 1, At(1, 2, 100, new Vector2(10, 0)));
        var before = Of(ManipulationState.Delta)[^1].TotalTranslation;

        Send(RawPointerEventType.Move, 0, At(0, 2, 100, new Vector2(30, 0)));
        var after = Of(ManipulationState.Delta)[^1];
        var completedBeforeLast = Of(ManipulationState.Completed).Count;
        Send(RawPointerEventType.Up, 0, At(0, 2, 100, new Vector2(30, 0)));

        Assert.Multiple(() =>
        {
            Assert.That(after.TotalTranslation.X - before.X, Is.EqualTo(20).Within(Pixel));
            Assert.That(after.Contacts, Is.EqualTo(1));
            Assert.That(completedBeforeLast, Is.Zero);
            Assert.That(Of(ManipulationState.Completed), Has.Count.EqualTo(1));
        });
    }

    // One finger's stroke is its own: a second finger resting down after it does not turn the way it came into a pan.
    [Test]
    public void AFingerThatMovedAlone_MakesNoGesture_WhenASecondLands()
    {
        Send(RawPointerEventType.Down, 0, new Vector2(200, 300));
        for (var x = 220; x <= 300; x += 20)
        {
            Send(RawPointerEventType.Move, 0, new Vector2(x, 300));
        }

        Send(RawPointerEventType.Down, 1, new Vector2(700, 300));
        Send(RawPointerEventType.Move, 0, new Vector2(301, 300));
        Send(RawPointerEventType.Move, 1, new Vector2(700, 301));
        Send(RawPointerEventType.Up, 1, new Vector2(700, 301));
        Send(RawPointerEventType.Up, 0, new Vector2(301, 300));

        Assert.That(_events, Is.Empty);
    }

    // Two fingers resting together on the pad may be a gesture about to start; one alone is just a finger again.
    [Test]
    public void FingersTogether_AreInAGesture_UntilOneIsLeftAlone()
    {
        Send(RawPointerEventType.Down, 0, new Vector2(200, 300));
        var alone = Finger(0).IsInGesture;
        Send(RawPointerEventType.Down, 1, new Vector2(700, 300));
        var together = (Finger(0).IsInGesture, Finger(1).IsInGesture);
        Send(RawPointerEventType.Up, 1, new Vector2(700, 300));
        var leftAlone = Finger(0).IsInGesture;
        Send(RawPointerEventType.Up, 0, new Vector2(200, 300));

        Assert.Multiple(() =>
        {
            Assert.That(alone, Is.False);
            Assert.That(together, Is.EqualTo((true, true)));
            Assert.That(leftAlone, Is.False);
        });
    }

    [Test]
    public void OnAnElementThatTakesNoGestures_FingersStayPointers()
    {
        Gestures.SetIsManipulationEnabled(_pad, false);
        var clicks = 0;
        _button.Click += (_, _) => clicks++;

        Send(RawPointerEventType.Down, 0, new Vector2(400, 300));
        Send(RawPointerEventType.Down, 1, new Vector2(700, 300));
        Send(RawPointerEventType.Move, 1, new Vector2(800, 300));
        Send(RawPointerEventType.Up, 1, new Vector2(800, 300));
        Send(RawPointerEventType.Up, 0, new Vector2(400, 300));

        Assert.Multiple(() =>
        {
            Assert.That(_events, Is.Empty);
            Assert.That(clicks, Is.EqualTo(1));
        });
    }

    private static Pointer Finger(int finger) =>
        PointerDevice.Current.Pointers.First(pointer => pointer.Id == FirstId + (uint)finger);

    private List<ManipulationEventArgs> Of(ManipulationState state) => [.. _events.Where(e => e.State == state)];

    private static Vector2 At(int finger, int fingers, double radius, Vector2 offset = default, double turn = 0)
    {
        var angle = (360.0 * finger / fingers + turn) * Math.PI / 180;
        return Middle + offset + new Vector2(Math.Cos(angle) * radius, Math.Sin(angle) * radius);
    }

    private void Down(int fingers, double radius)
    {
        for (var i = 0; i < fingers; i++)
        {
            Send(RawPointerEventType.Down, i, At(i, fingers, radius));
        }
    }

    private void MoveAll(int fingers, double radius, Vector2 offset = default, double turn = 0)
    {
        for (var i = 0; i < fingers; i++)
        {
            Send(RawPointerEventType.Move, i, At(i, fingers, radius, offset, turn));
        }
    }

    private void Up(int fingers, double radius, Vector2 offset = default, double turn = 0)
    {
        for (var i = 0; i < fingers; i++)
        {
            Send(RawPointerEventType.Up, i, At(i, fingers, radius, offset, turn));
        }
    }

    private void Send(RawPointerEventType type, int finger, Vector2 at) =>
        PointerDevice.Current.ProcessEvent(new RawPointerEventArgs(type, _window, at, InputModifiers.None, 0)
        {
            PointerId = FirstId + (uint)finger,
            PointerType = PointerType.Touch,
            IsPrimary = finger == 0
        });
}
