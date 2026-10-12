using System;
using Adamantium.Core.Events;
using Adamantium.Mathematics;
using Adamantium.Multiverse;
using Adamantium.Multiverse.Input;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Input.Raw;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Animation;
using Adamantium.UI.Extensions;
using Adamantium.UI.Universes;
using NUnit.Framework;
using GestureEvent = Adamantium.UI.Core.Input.Gestures;

namespace Adamantium.UITests;

/// <summary>A gesture of fingers on the element a universe is shown in reaches the universe's input as the engine reads
/// it: the move in the output's units, the pinch in doublings, the turn in degrees.</summary>
[TestFixture]
public class UniverseGestureTests
{
    private const uint FirstId = 120;

    private Window _window;
    private RenderTargetUniverseOutput _output;
    private TimeSpan _holdingTime;

    [SetUp]
    public void SetUp()
    {
        _holdingTime = GestureEvent.HoldingTime;
        GestureEvent.HoldingTime = TimeSpan.FromHours(1);

        var view = new RenderTargetPanel { Background = Brushes.Gray };
        _window = new Window { Width = 1000, Height = 600, Content = view };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(_window);
        }

        var events = new EventAggregator();
        _output = new RenderTargetUniverseOutput(events, new OutputContext(view));
        _output.Input = new InputWormhole(_output, new GamepadHub(new NoGamepadBackend(), events));
    }

    [TearDown]
    public void TearDown()
    {
        _output.Dispose();
        GestureEvent.HoldingTime = _holdingTime;
        AnimationManager.Reset();
    }

    [Test]
    public void FingersSpreading_ReachTheUniverse_AsAPinch()
    {
        Send(RawPointerEventType.Down, 0, new Vector2(400, 300));
        Send(RawPointerEventType.Down, 1, new Vector2(600, 300));
        for (var step = 1; step <= 5; step++)
        {
            Send(RawPointerEventType.Move, 0, new Vector2(400 - 20 * step, 300));
            Send(RawPointerEventType.Move, 1, new Vector2(600 + 20 * step, 300));
        }

        _output.Input.Update(default);
        Send(RawPointerEventType.Up, 0, new Vector2(300, 300));
        Send(RawPointerEventType.Up, 1, new Vector2(700, 300));

        Assert.Multiple(() =>
        {
            Assert.That(_output.Input.GesturePinch, Is.EqualTo(1).Within(0.02),
                "from 200 apart to 400, recognized on the first step: one doubling");
            Assert.That(_output.Input.GesturePan.Length(), Is.LessThan(1e-3));
            Assert.That(_output.Input.GestureRotation, Is.Zero);
        });
    }

    [Test]
    public void FingersMovingTogether_ReachTheUniverse_AsAPan()
    {
        Send(RawPointerEventType.Down, 0, new Vector2(400, 300));
        Send(RawPointerEventType.Down, 1, new Vector2(600, 300));
        for (var step = 1; step <= 4; step++)
        {
            Send(RawPointerEventType.Move, 0, new Vector2(400 + 10 * step, 300));
            Send(RawPointerEventType.Move, 1, new Vector2(600 + 10 * step, 300));
        }

        _output.Input.Update(default);
        var pan = _output.Input.GesturePan;
        var over = _output.IsPointerOver;
        Send(RawPointerEventType.Up, 0, new Vector2(440, 300));
        Send(RawPointerEventType.Up, 1, new Vector2(640, 300));
        _output.Input.Update(default);

        Assert.Multiple(() =>
        {
            Assert.That(pan, Is.EqualTo(new Vector2F(40, 0) * _output.PixelsPerPoint));
            Assert.That(over, Is.True, "the output holds the fingers, the mouse anywhere");
            Assert.That(_output.Input.IsMouseButtonDown(MouseButton.Left), Is.False,
                "the press the fingers made before the gesture took them is let go");
        });
    }

    private void Send(RawPointerEventType type, int finger, Vector2 at) =>
        PointerDevice.Current.ProcessEvent(new RawPointerEventArgs(type, _window, at, InputModifiers.None, 0)
        {
            PointerId = FirstId + (uint)finger,
            PointerType = PointerType.Touch,
            IsPrimary = finger == 0
        });
}
