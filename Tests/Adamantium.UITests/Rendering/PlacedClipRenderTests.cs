using System;
using System.Runtime.InteropServices;
using Adamantium.Graphics.Core;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

// A clip moving with a placement cuts what is drawn inside it, and moves with the placement as the drawings do: a slot
// write and a replay - the element is not drawn again.
[TestFixture]
[Category("Gpu")]
public class PlacedClipRenderTests
{
    private const int Width = 160;
    private const int Height = 96;

    public enum Shaped
    {
        Geometry,
        Rectangle,
        Ellipse,
        Stroke
    }

    private sealed class ClippedShapes : MeasurableUIComponent
    {
        public readonly Placement Placement = new();
        public int Renders;
        public Shaped Shape = Shaped.Geometry;
        public Rect Clip = new(0, 0, 30, 30);
        public CornerRadius Corners = CornerRadius.Empty;
        public Rect? InnerClip;

        protected override void OnRender(IDrawingContext context)
        {
            base.OnRender(context);
            Renders++;
            var session = context.ForControl(this);
            var shape = new Rect(-20, -20, 80, 80);
            session.PushClip(Clip, Corners, Placement);
            if (InnerClip is { } inner)
            {
                session.PushClip(inner, CornerRadius.Empty, Placement);
            }

            switch (Shape)
            {
                case Shaped.Rectangle:
                    session.DrawRectangle(Brushes.White, shape, CornerRadius.Empty, null, Placement);
                    break;
                case Shaped.Ellipse:
                    session.DrawEllipse(new Rect(-60, -60, 160, 160), Brushes.White, null, Placement);
                    break;
                case Shaped.Stroke:
                    // A line as wide as the clip and longer than it every way.
                    var line = new StreamGeometry();
                    line.Open().BeginFigure(new Vector2<double>(-40, 15), false, false)
                        .PolylineLineTo([new Vector2<double>(80, 15)], true);
                    session.DrawGeometry(null, line, new Pen(Brushes.White, 60), Placement);
                    break;
                default:
                    session.DrawGeometry(Brushes.White, new RectangleGeometry(shape), null, Placement);
                    break;
            }

            if (InnerClip != null)
            {
                session.PopClip();
            }

            session.PopClip();
        }
    }

    private readonly record struct Lit(int Count, int Left, int Top, int Right, int Bottom);

    private OffscreenTestRenderer _renderer;
    private VisualRoot _root;
    private ClippedShapes _shapes;

    [SetUp]
    public void SetUp()
    {
        var device = GpuTestDevice.Device;
        _renderer = new OffscreenTestRenderer(device, new RenderUnitFactory(device, new DeviceResourceFactory(device)),
            Width, Height) { ClearColor = Colors.Black };
        _shapes = new ClippedShapes { Width = Width, Height = Height };
        var canvas = new Canvas();
        canvas.Children.Add(_shapes);
        _root = new VisualRoot(canvas, Width, Height);
    }

    [TearDown]
    public void TearDown() => _renderer.Dispose();

    private static Matrix4x4F At(float x, float y) => Matrix4x4.CreateTranslation<float>(x, y, 0);

    private Lit Draw()
    {
        ((IMeasurableComponent)_root).Measure(new Size<double>(Width, Height));
        ((IMeasurableComponent)_root).Arrange(new Rect(0, 0, Width, Height));
        Assert.That(_renderer.RenderFrame(_root), Is.True, "off-screen frame must render");
        RenderDirty.Clear();
        return Shown();
    }

    private byte[] Pixels()
    {
        using var img = _renderer.RenderTarget.ResolveTexture.ToImage();
        var pixels = new byte[(int)img.TotalSizeInBytes];
        Marshal.Copy(img.DataPointer, pixels, 0, pixels.Length);
        return pixels;
    }

    private Lit Shown()
    {
        var pixels = Pixels();
        int count = 0, left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i] < 128 && pixels[i + 1] < 128 && pixels[i + 2] < 128)
            {
                continue;
            }

            var x = i / 4 % Width;
            var y = i / 4 / Width;
            count++;
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x);
            bottom = Math.Max(bottom, y);
        }

        return new Lit(count, left, top, right, bottom);
    }

    private int Brightness(int x, int y) => Pixels()[(y * Width + x) * 4];

    [TestCase(Shaped.Geometry)]
    [TestCase(Shaped.Rectangle)]
    [TestCase(Shaped.Ellipse)]
    [TestCase(Shaped.Stroke)]
    public void AShapeLargerThanItsClip_IsCutToIt_AndTheCutMovesWithThePlacement(Shaped shape)
    {
        _shapes.Shape = shape;
        _shapes.Placement.Matrix = At(10, 10);
        var before = Draw();
        var renders = _shapes.Renders;

        _shapes.Placement.Matrix = At(70, 40);
        var moved = Draw();
        var replayed = _renderer.Cache.LastFrameReplayed;

        Assert.Multiple(() =>
        {
            Assert.That(before.Left, Is.EqualTo(10).Within(1), "cut at the clip's left, not the shape's");
            Assert.That(before.Right, Is.EqualTo(39).Within(1), "the clip is 30 wide");
            Assert.That(before.Bottom, Is.EqualTo(39).Within(1));
            Assert.That(moved.Left - before.Left, Is.EqualTo(60).Within(1), "the cut follows the placement");
            Assert.That(moved.Top - before.Top, Is.EqualTo(30).Within(1));
            Assert.That(moved.Count, Is.EqualTo(before.Count).Within(4), "the same cut, only moved");
            Assert.That(_shapes.Renders, Is.EqualTo(renders), "a move is not a record");
            Assert.That(replayed, Is.True, "a move is a replay, not a walk");
        });
    }

    // A shape no batch takes is drawn by its own unit, cut by the scissor round its clip - moved and replayed too.
    [Test]
    public void AShapeDrawnByItsOwnUnit_IsCutToo_AndFollows()
    {
        var instancingWas = Adamantium.UI.Rendering.Retained.InstancedFillCollector.Enabled;
        Adamantium.UI.Rendering.Retained.InstancedFillCollector.Enabled = false;
        try
        {
            _shapes.Placement.Matrix = At(10, 10);
            var before = Draw();
            _shapes.Placement.Matrix = At(70, 40);
            var moved = Draw();

            Assert.Multiple(() =>
            {
                Assert.That(before.Left, Is.EqualTo(10).Within(1));
                Assert.That(before.Right, Is.EqualTo(39).Within(1));
                Assert.That(moved.Left, Is.EqualTo(70).Within(1));
                Assert.That(moved.Bottom, Is.EqualTo(69).Within(1));
            });
        }
        finally
        {
            Adamantium.UI.Rendering.Retained.InstancedFillCollector.Enabled = instancingWas;
        }
    }

    [Test]
    public void AScaledPlacement_ScalesTheCut()
    {
        _shapes.Placement.Matrix = Matrix4x4.CreateScale<float>(2, 2, 1) * At(10, 10);

        var lit = Draw();

        Assert.Multiple(() =>
        {
            Assert.That(lit.Left, Is.EqualTo(10).Within(1));
            Assert.That(lit.Right, Is.EqualTo(69).Within(1), "30 units at twice the size");
            Assert.That(lit.Bottom, Is.EqualTo(69).Within(1));
        });
    }

    [Test]
    public void AQuarterTurn_TurnsTheCut()
    {
        _shapes.Clip = new Rect(0, 0, 40, 20);
        _shapes.Placement.Matrix = Matrix4x4.CreateRotationZ<float>(MathF.PI / 2) * At(60, 10);

        var lit = Draw();

        Assert.Multiple(() =>
        {
            Assert.That(lit.Right - lit.Left, Is.EqualTo(19).Within(1), "the wide clip stands tall");
            Assert.That(lit.Bottom - lit.Top, Is.EqualTo(39).Within(1));
        });
    }

    [Test]
    public void AClipInsideAnother_CutsByBoth()
    {
        _shapes.InnerClip = new Rect(15, -10, 40, 40);
        _shapes.Placement.Matrix = At(10, 10);

        var lit = Draw();

        Assert.Multiple(() =>
        {
            Assert.That(lit.Left, Is.EqualTo(25).Within(1), "the inner clip's left");
            Assert.That(lit.Top, Is.EqualTo(10).Within(1), "the outer clip's top");
            Assert.That(lit.Right, Is.EqualTo(39).Within(1), "the outer clip's right");
        });
    }

    [Test]
    public void AClipMovedOffTheSurface_LeavesNothing_AndBackOn_ShowsTheCutAgain()
    {
        _shapes.Placement.Matrix = At(10, 10);
        var shown = Draw();

        _shapes.Placement.Matrix = At(400, 10);
        var off = Draw();
        _shapes.Placement.Matrix = At(10, 10);
        var back = Draw();

        Assert.Multiple(() =>
        {
            Assert.That(off.Count, Is.Zero, "the shape reaches back over the surface, its clip does not");
            Assert.That(back, Is.EqualTo(shown));
        });
    }

    [Test]
    public void RoundedCorners_RoundTheCut()
    {
        _shapes.Corners = new CornerRadius(10);
        _shapes.Placement.Matrix = At(10, 10);

        Draw();

        Assert.Multiple(() =>
        {
            Assert.That(Brightness(10, 10), Is.LessThan(64), "the corner is cut round");
            Assert.That(Brightness(25, 25), Is.GreaterThan(192), "the middle is not");
        });
    }
}
