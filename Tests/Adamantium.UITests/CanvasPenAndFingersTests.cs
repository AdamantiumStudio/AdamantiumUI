using System;
using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.DrawingBoard;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Input.Raw;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Animation;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>Ink on the canvas from a pen and from fingers: each contact draws a stroke of its own, a pen draws as wide as
/// it presses and erases with its other end, <see cref="InfiniteCanvas.FingerInk"/> says whether fingers draw, and a
/// pinch takes its own fingers' strokes back while a pen beside them goes on.</summary>
[TestFixture]
public class CanvasPenAndFingersTests
{
    private const uint FingerId = 140;
    private const uint PenId = 150;

    private readonly System.Collections.Generic.HashSet<uint> _down = new();
    private Window _window;
    private InfiniteCanvas _canvas;
    private CanvasScene _scene;
    private TimeSpan _holdingTime;

    [SetUp]
    public void SetUp()
    {
        _holdingTime = Gestures.HoldingTime;
        Gestures.HoldingTime = TimeSpan.FromHours(1);

        _scene = new CanvasScene();
        _canvas = new InfiniteCanvas
        {
            Ink = new SolidColorBrush(Colors.Red),
            InkThickness = 4,
            Mode = CanvasMode.Drawing,
            Tool = new PenTool(),
            Background = Brushes.White,
            Scene = _scene
        };
        _window = new Window { Width = 1000, Height = 600, Content = _canvas };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(_window);
        }
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var left in PointerDevice.Current.Pointers.Where(p => p.Id >= FingerId && p.Id <= PenId).ToList())
        {
            Send(RawPointerEventType.Cancel, left.Id, left.Type, Vector2<double>.Zero);
        }

        Gestures.HoldingTime = _holdingTime;
        AnimationManager.Reset();
    }

    private StrokeItem[] Strokes => [.. _scene.Items.OfType<StrokeItem>()];

    [Test]
    public void FingersDown_TogetherEachDrawAStrokeOfTheirOwn([Values(2, 5)] int fingers)
    {
        for (var i = 0; i < fingers; i++)
        {
            Send(RawPointerEventType.Down, FingerId + (uint)i, PointerType.Touch, new Vector2<double>(100 + 150 * i, 100));
        }

        for (var step = 1; step <= 4; step++)
        {
            for (var i = 0; i < fingers; i++)
            {
                Send(RawPointerEventType.Move, FingerId + (uint)i, PointerType.Touch, new Vector2<double>(100 + 150 * i, 100 + 30 * step));
            }
        }

        for (var i = 0; i < fingers; i++)
        {
            Send(RawPointerEventType.Up, FingerId + (uint)i, PointerType.Touch, new Vector2<double>(100 + 150 * i, 220));
        }

        Assert.That(Strokes, Has.Length.EqualTo(fingers), "a stroke for each finger, and no pinch among straight lines");
    }

    [Test]
    public void APen_DrawsAsWideAsItPresses_AndAFingerAtFullWidth()
    {
        Stroke(PenId, PointerType.Pen, new Vector2<double>(100, 100), new Vector2<double>(300, 100), pressure: 0.3f);
        Stroke(FingerId, PointerType.Touch, new Vector2<double>(100, 300), new Vector2<double>(300, 300));

        Assert.Multiple(() =>
        {
            Assert.That(Strokes, Has.Length.EqualTo(2));
            Assert.That(Strokes[0].Points.Select(p => p.Pressure), Is.All.EqualTo(0.6f).Within(1e-4),
                "0.3 against a pen's usual 0.5");
            Assert.That(Strokes[1].Points.Select(p => p.Pressure), Is.All.EqualTo(1f), "a finger knows no pressure");
        });
    }

    // A straight line pressed hard in its middle keeps its middle: smoothing drops points that change nothing, and a
    // point where the width changes changes something.
    [Test]
    public void APressureBumpOnAStraightLine_SurvivesSmoothing()
    {
        var stroke = new StrokeItem(Vector2<double>.Zero, Brushes.Black, 10);
        for (var i = 0; i <= 20; i++)
        {
            stroke.Add(new Vector2<double>(i * 10, 0), 0.2f + 0.8f * (1 - Math.Abs(i - 10) / 10f));
        }

        stroke.Smooth(0.6, 4);

        Assert.That(stroke.Points.Max(point => point.Pressure), Is.GreaterThan(0.9f));
    }

    [Test]
    public void APenUpsideDown_Erases_WhateverTheToolInHand()
    {
        Stroke(FingerId, PointerType.Touch, new Vector2<double>(100, 300), new Vector2<double>(600, 300));
        var drawn = Strokes.Length;

        Stroke(PenId, PointerType.Pen, new Vector2<double>(350, 200), new Vector2<double>(350, 400), eraser: true);

        Assert.Multiple(() =>
        {
            Assert.That(drawn, Is.EqualTo(1));
            Assert.That(Strokes, Has.Length.EqualTo(2), "rubbed through the middle, the stroke is two");
            Assert.That(_canvas.Tool, Is.InstanceOf<PenTool>(), "the pen in hand stays the pen");
        });
    }

    [Test]
    public void FingersThatMayNotInk_MoveThePlane()
    {
        _canvas.FingerInk = CanvasFingerInk.Never;
        var before = _canvas.Offset;

        Stroke(FingerId, PointerType.Touch, new Vector2<double>(300, 300), new Vector2<double>(380, 340));

        Assert.Multiple(() =>
        {
            Assert.That(Strokes, Is.Empty);
            Assert.That(_canvas.Offset - before, Is.EqualTo(new Vector2<double>(80, 40)));
        });
    }

    [Test]
    public void FingersInkWhenNoPen_StopWhileAPenIsInReach()
    {
        _canvas.FingerInk = CanvasFingerInk.WhenNoPen;

        Send(RawPointerEventType.Move, PenId, PointerType.Pen, new Vector2<double>(800, 500));
        Stroke(FingerId, PointerType.Touch, new Vector2<double>(100, 100), new Vector2<double>(200, 100));
        var withPen = Strokes.Length;

        Send(RawPointerEventType.LeaveWindow, PenId, PointerType.Pen, new Vector2<double>(800, 500));
        Stroke(FingerId, PointerType.Touch, new Vector2<double>(100, 300), new Vector2<double>(200, 300));

        Assert.Multiple(() =>
        {
            Assert.That(withPen, Is.Zero, "a pen in reach: the finger moved the plane");
            Assert.That(Strokes, Has.Length.EqualTo(1), "the pen gone: the finger draws");
        });
    }

    // Two fingers that pinch take their own strokes back with them; the pen drawing beside them keeps its stroke.
    [Test]
    public void APinch_TakesBackItsFingersStrokes_AndThePenGoesOn()
    {
        Send(RawPointerEventType.Down, PenId, PointerType.Pen, new Vector2<double>(800, 100), 0.8f);
        Send(RawPointerEventType.Move, PenId, PointerType.Pen, new Vector2<double>(800, 140), 0.8f);

        Send(RawPointerEventType.Down, FingerId, PointerType.Touch, new Vector2<double>(400, 300));
        Send(RawPointerEventType.Down, FingerId + 1, PointerType.Touch, new Vector2<double>(500, 300));
        for (var step = 1; step <= 4; step++)
        {
            Send(RawPointerEventType.Move, FingerId, PointerType.Touch, new Vector2<double>(400 - 15 * step, 300));
            Send(RawPointerEventType.Move, FingerId + 1, PointerType.Touch, new Vector2<double>(500 + 15 * step, 300));
        }

        var scale = _canvas.Scale;
        Send(RawPointerEventType.Up, FingerId, PointerType.Touch, new Vector2<double>(340, 300));
        Send(RawPointerEventType.Up, FingerId + 1, PointerType.Touch, new Vector2<double>(560, 300));

        Send(RawPointerEventType.Move, PenId, PointerType.Pen, new Vector2<double>(800, 200), 0.8f);
        Send(RawPointerEventType.Up, PenId, PointerType.Pen, new Vector2<double>(800, 200), 0.8f);

        Assert.Multiple(() =>
        {
            Assert.That(scale, Is.GreaterThan(1.2), "the fingers zoomed");
            Assert.That(Strokes, Has.Length.EqualTo(1), "only the pen's stroke is left");
            Assert.That(Strokes[0].Points.Select(p => p.Pressure), Is.All.EqualTo(1.6f).Within(1e-4));
        });
    }

    // A pen put down while a finger moves the plane draws its own stroke; the plane follows the finger alone.
    [Test]
    public void APen_WhileAFingerMovesThePlane_DrawsItsOwnStroke()
    {
        _canvas.FingerInk = CanvasFingerInk.Never;
        var before = _canvas.Offset;

        Send(RawPointerEventType.Down, FingerId, PointerType.Touch, new Vector2<double>(100, 400));
        Send(RawPointerEventType.Move, FingerId, PointerType.Touch, new Vector2<double>(130, 400));
        Stroke(PenId, PointerType.Pen, new Vector2<double>(500, 100), new Vector2<double>(700, 200));
        Send(RawPointerEventType.Up, FingerId, PointerType.Touch, new Vector2<double>(130, 400));

        Assert.Multiple(() =>
        {
            Assert.That(Strokes, Has.Length.EqualTo(1));
            Assert.That(_canvas.Offset - before, Is.EqualTo(new Vector2<double>(30, 0)), "only the finger moved the plane");
            Assert.That(_canvas.History.CanUndo, Is.True, "the pen's stroke is a step to take back");
        });
    }

    // A palm landing while the pen writes leaves no mark and moves nothing: the pen's stroke is whole.
    [Test]
    public void APalm_WhileThePenWrites_IsIgnored()
    {
        _canvas.FingerInk = CanvasFingerInk.WhenNoPen;
        var before = _canvas.Offset;

        Send(RawPointerEventType.Down, PenId, PointerType.Pen, new Vector2<double>(300, 100));
        Send(RawPointerEventType.Move, PenId, PointerType.Pen, new Vector2<double>(350, 100));
        Send(RawPointerEventType.Down, FingerId, PointerType.Touch, new Vector2<double>(400, 400));
        Send(RawPointerEventType.Move, FingerId, PointerType.Touch, new Vector2<double>(420, 410));
        Send(RawPointerEventType.Move, PenId, PointerType.Pen, new Vector2<double>(450, 100));
        Send(RawPointerEventType.Up, FingerId, PointerType.Touch, new Vector2<double>(420, 410));
        Send(RawPointerEventType.Move, PenId, PointerType.Pen, new Vector2<double>(550, 100));
        Send(RawPointerEventType.Up, PenId, PointerType.Pen, new Vector2<double>(550, 100));
        Send(RawPointerEventType.LeaveWindow, PenId, PointerType.Pen, new Vector2<double>(550, 100));

        Assert.Multiple(() =>
        {
            Assert.That(Strokes, Has.Length.EqualTo(1));
            Assert.That(Strokes[0].Bounds.Width, Is.GreaterThan(200), "the pen's stroke ran on past the palm");
            Assert.That(_canvas.Offset, Is.EqualTo(before));
        });
    }

    // Two people drawing apart, one after the other has begun, each draw a line: nothing pinches.
    [Test]
    public void TwoPeopleDrawingApart_DoNotPinch()
    {
        var scale = _canvas.Scale;
        Send(RawPointerEventType.Down, FingerId, PointerType.Touch, new Vector2<double>(400, 300));
        Send(RawPointerEventType.Move, FingerId, PointerType.Touch, new Vector2<double>(380, 300));
        Send(RawPointerEventType.Down, FingerId + 1, PointerType.Touch, new Vector2<double>(500, 300));
        for (var step = 1; step <= 6; step++)
        {
            Send(RawPointerEventType.Move, FingerId, PointerType.Touch, new Vector2<double>(380 - 20 * step, 300));
            Send(RawPointerEventType.Move, FingerId + 1, PointerType.Touch, new Vector2<double>(500 + 20 * step, 300 + 10 * step));
        }

        Send(RawPointerEventType.Up, FingerId, PointerType.Touch, new Vector2<double>(260, 300));
        Send(RawPointerEventType.Up, FingerId + 1, PointerType.Touch, new Vector2<double>(620, 360));

        Assert.Multiple(() =>
        {
            Assert.That(Strokes, Has.Length.EqualTo(2));
            Assert.That(_canvas.Scale, Is.EqualTo(scale));
        });
    }

    // A pen pressing harder along a straight line widens smoothly, not in one step at its end.
    [Test]
    public void APressureRamp_StaysARamp_ThroughSmoothing()
    {
        var stroke = new StrokeItem(Vector2<double>.Zero, Brushes.Black, 10);
        for (var i = 0; i <= 20; i++)
        {
            stroke.Add(new Vector2<double>(i * 10, 0), 0.2f + 0.04f * i);
        }

        stroke.Smooth(0.6, 4);

        var points = stroke.Points;
        var after = Enumerable.Range(1, points.Count - 1).First(i => points[i].At.X >= 100);
        var (left, right) = (points[after - 1], points[after]);
        var t = (100 - left.At.X) / (right.At.X - left.At.X);
        Assert.That(left.Pressure + (right.Pressure - left.Pressure) * t, Is.EqualTo(0.6f).Within(0.05f),
            "the width halfway along is halfway between");
    }

    // Round, with a bend to keep the smoothing's samples, carries the pressure between points rather than holding it.
    [Test]
    public void APressureRamp_RoundACorner_StaysARamp()
    {
        var stroke = new StrokeItem(Vector2<double>.Zero, Brushes.Black, 10);
        for (var i = 0; i <= 10; i++)
        {
            stroke.Add(new Vector2<double>(i * 10, 0), 0.2f + 0.04f * i);
        }

        for (var i = 1; i <= 10; i++)
        {
            stroke.Add(new Vector2<double>(100, i * 10), 0.6f + 0.04f * i);
        }

        stroke.Smooth(0.6, 4);

        var pressures = stroke.Points.Select(point => point.Pressure).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(pressures.Distinct().Count(), Is.GreaterThan(4), "more than the kept points' own values");
            Assert.That(pressures.Zip(pressures.Skip(1)).All(pair => pair.Second >= pair.First - 1e-4f), Is.True,
                "rising all the way");
        });
    }

    [Test]
    public void Erasing_KeepsWhatThePiecesWerePressedWith()
    {
        var stroke = new StrokeItem(Vector2<double>.Zero, Brushes.Black, 4);
        for (var i = 0; i <= 10; i++)
        {
            stroke.Add(new Vector2<double>(i * 10, 0), 0.4f);
        }

        var pieces = new System.Collections.Generic.List<StrokeItem>();
        stroke.Erase(new Vector2<double>(50, 0), 5, pieces);

        Assert.Multiple(() =>
        {
            Assert.That(pieces, Has.Count.EqualTo(2));
            Assert.That(pieces.SelectMany(piece => piece.Points).Select(point => point.Pressure), Is.All.EqualTo(0.4f).Within(1e-4));
        });
    }

    [Test]
    public void SavedAndLoaded_AStrokeKeepsItsPressure()
    {
        var stroke = new StrokeItem(Vector2<double>.Zero, new SolidColorBrush(Colors.Red), 4);
        stroke.Add(new Vector2<double>(0, 0), 0.3f);
        stroke.Add(new Vector2<double>(50, 0), 1.2f);

        var loaded = (StrokeItem)CanvasSvg.Load(CanvasSvg.Save([stroke])).Single();

        Assert.That(loaded.Points.Select(point => point.Pressure), Is.EqualTo(new[] { 0.3f, 1.2f }).Within(1e-3));
    }

    private void Stroke(uint id, PointerType type, Vector2<double> from, Vector2<double> to, float pressure = 0.5f, bool eraser = false)
    {
        Send(RawPointerEventType.Down, id, type, from, pressure, eraser);
        for (var step = 1; step <= 8; step++)
        {
            Send(RawPointerEventType.Move, id, type, from + (to - from) * (step / 8.0), pressure, eraser);
        }

        Send(RawPointerEventType.Up, id, type, to, pressure, eraser);
        if (type == PointerType.Pen)
        {
            Send(RawPointerEventType.LeaveWindow, id, type, to, pressure, eraser);
        }
    }

    private void Send(RawPointerEventType kind, uint id, PointerType type, Vector2<double> at, float pressure = 0.5f,
        bool eraser = false)
    {
        if (kind == RawPointerEventType.Down) _down.Add(id);
        if (kind is RawPointerEventType.Up or RawPointerEventType.Cancel) _down.Remove(id);

        PointerDevice.Current.ProcessEvent(new RawPointerEventArgs(kind, _window, at, InputModifiers.None, 0)
        {
            PointerId = id,
            PointerType = type,
            IsPrimary = true,
            Properties = new PointerPointProperties
            {
                Pressure = pressure,
                IsEraser = eraser,
                IsLeftButtonPressed = _down.Contains(id)
            }
        });
    }
}
