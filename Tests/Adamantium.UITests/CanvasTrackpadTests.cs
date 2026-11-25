using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Canvases;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Input.Raw;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Animation;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>Two fingers on a trackpad move a canvas whose wheel zooms - one that inks too, as they draw nothing - and a
/// pinch zooms it; a canvas whose wheel scrolls is scrolled by them.</summary>
[TestFixture]
public class CanvasTrackpadTests
{
    private static readonly Vector2<double> Middle = new(500, 300);

    private Window _window;
    private InfiniteCanvas _canvas;

    [SetUp]
    public void SetUp()
    {
        _canvas = new DrawingBoard { Background = Brushes.White, Scene = new CanvasScene(), Tool = new PenTool() };
        _window = new Window { Width = 1000, Height = 600, Content = _canvas };
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(_window);
        }
    }

    [TearDown]
    public void TearDown()
    {
        Send(RawTrackpadEventType.Completed, ManipulationKinds.Pan);
        Send(RawTrackpadEventType.Completed, ManipulationKinds.Pinch);
        AnimationManager.Reset();
    }

    [Test]
    public void TwoFingers_MoveTheCanvasWithThem()
    {
        var point = _canvas.ScreenToWorld(Middle);

        Send(RawTrackpadEventType.Started, ManipulationKinds.Pan);
        var taken = Send(RawTrackpadEventType.Delta, ManipulationKinds.Pan, new Vector2<double>(40, -25));

        var now = _canvas.WorldToScreen(point);
        Assert.Multiple(() =>
        {
            Assert.That(taken, Is.True, "no wheel zooms besides");
            Assert.That(now.X, Is.EqualTo(Middle.X + 40).Within(1e-6));
            Assert.That(now.Y, Is.EqualTo(Middle.Y - 25).Within(1e-6));
            Assert.That(_canvas.Scale, Is.EqualTo(1));
        });
    }

    [Test]
    public void APinch_Zooms()
    {
        Send(RawTrackpadEventType.Started, ManipulationKinds.Pinch);
        Send(RawTrackpadEventType.Delta, ManipulationKinds.Pinch, scale: 1.5);

        Assert.That(_canvas.Scale, Is.EqualTo(1.5).Within(1e-6));
    }

    [TestCase(CanvasWheelMode.Scroll)]
    [TestCase(CanvasWheelMode.None)]
    public void ACanvasWhoseWheelDoesNotZoom_LeavesTwoFingersToTheWheel(CanvasWheelMode mode)
    {
        _canvas.WheelMode = mode;

        Assert.That(Send(RawTrackpadEventType.Started, ManipulationKinds.Pan), Is.False);
    }

    private bool Send(RawTrackpadEventType type, ManipulationKinds kind, Vector2<double> translation = default,
        double scale = 1) =>
        PointerDevice.Current.ProcessTrackpad(new RawTrackpadEventArgs(type, kind, _window, Middle, InputModifiers.None, 0)
        {
            Translation = translation, Scale = scale
        });
}
