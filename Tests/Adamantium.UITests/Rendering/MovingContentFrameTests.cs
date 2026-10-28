using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Animation;
using Adamantium.UI.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

// What changes with time alone - a living aura, a flowing noise, a morphing fractal - is drawn from a clock nobody marks
// dirty, so the frame that drew it has to ask for the next one, or a window that draws only when asked freezes it.
[TestFixture]
[Category("Gpu")]
public class MovingContentFrameTests
{
    private const int Dim = 96;

    private OffscreenTestRenderer _renderer;

    [SetUp]
    public void SetUp()
    {
        var device = GpuTestDevice.Device;
        _renderer = new OffscreenTestRenderer(device, new RenderUnitFactory(device, new DeviceResourceFactory(device)), Dim, Dim);
    }

    [TearDown]
    public void TearDown()
    {
        _renderer?.Dispose();
        _renderer = null;
        AnimationManager.Reset();
    }

    private bool Moves(System.Action<IDrawingSession> draw, Aura aura = null)
    {
        var control = new TestControl { RenderAction = s => draw(s), Aura = aura };
        control.Bounds = new Rect(0, 0, 40, 40);
        control.RenderSize = new Size<double>(40, 40);
        control.RenderTransform = new Transform { TranslateX = 28, TranslateY = 28 };
        var root = new VisualRoot(control, Dim, Dim);
        Assert.That(_renderer.RenderFrame(root), Is.True);
        Assert.That(_renderer.RenderFrame(root), Is.True);
        return _renderer.Cache.Moves;
    }

    private static Aura Living(double flow) => new() { Radius = 12, Turbulence = 1, Flow = flow, Color = Colors.Red };

    [Test]
    public void ALivingAura_Moves_WhileItFlows()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Moves(s => s.DrawRectangle(Brushes.White, new Rect(0, 0, 40, 40)), Living(1)), Is.True);
            Assert.That(Moves(s => s.DrawRectangle(Brushes.White, new Rect(0, 0, 40, 40)), Living(0)), Is.False,
                "at Flow 0 the wander holds still");
            Assert.That(Moves(s => s.DrawRectangle(Brushes.White, new Rect(0, 0, 40, 40)),
                new Aura { Radius = 12, Color = Colors.Red }), Is.False, "a still aura");
        });
    }

    [Test]
    public void AFlowingNoise_Moves_OnARectangleAndOnAGeometry()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Moves(s => s.DrawRectangle(new NoiseBrush { Animate = true }, new Rect(0, 0, 40, 40))), Is.True);
            Assert.That(Moves(s => s.DrawRectangle(new NoiseBrush(), new Rect(0, 0, 40, 40))), Is.False, "a still noise");
            Assert.That(Moves(s => s.DrawRectangle(new PatternBrush { Pattern = PatternType.Hatch }, new Rect(0, 0, 40, 40))),
                Is.False, "a pattern's pack is not a noise's flag");
            Assert.That(Moves(s => s.DrawGeometry(new NoiseBrush { Animate = true },
                new EllipseGeometry(new Rect(0, 0, 40, 40)))), Is.True, "a tessellated shape");
        });
    }

    [Test]
    public void AMorphingFractal_Moves()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Moves(s => s.DrawRectangle(new FractalBrush { Animate = true }, new Rect(0, 0, 40, 40))), Is.True);
            Assert.That(Moves(s => s.DrawRectangle(new FractalBrush(), new Rect(0, 0, 40, 40))), Is.False);
        });
    }

    // A stopped clock draws the same picture again: asking for it would keep the window drawing for nothing.
    [Test]
    public void AFlowAtSpeedZero_AsksForNothing()
    {
        Assert.That(Moves(s => s.DrawRectangle(new NoiseBrush { Animate = true, FlowSpeed = 0 }, new Rect(0, 0, 40, 40))),
            Is.False);
    }

    [Test]
    public void AfterTheAnimationsAreReset_AClockStartsAgain()
    {
        _ = new NoiseBrush { Animate = true };
        AnimationManager.Reset();

        Assert.That(Moves(s => s.DrawRectangle(new NoiseBrush { Animate = true }, new Rect(0, 0, 40, 40))), Is.True,
            "the reset dropped the ticker; a new animating brush has to be able to start it");
    }

    [Test]
    public void AnAnimationSwitchedOff_StopsMoving()
    {
        var brush = new NoiseBrush { Animate = true };
        var root = new TestRoot(Dim, Dim);
        var rectangle = new Adamantium.UI.Controls.Shapes.Rectangle { Fill = brush };
        root.Add(rectangle);
        rectangle.Measure(new Size<double>(40, 40));
        rectangle.Arrange(new Rect(4, 4, 40, 40));
        _renderer.RenderFrame(root);
        _renderer.RenderFrame(root);
        Assert.That(_renderer.Cache.Moves, Is.True);

        brush.Animate = false;
        _renderer.RenderFrame(root);
        _renderer.RenderFrame(root);

        Assert.That(_renderer.Cache.Moves, Is.False, "the window would go on drawing for nothing");
    }
}
