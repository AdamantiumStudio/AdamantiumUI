using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

// A stroke of many contours - a map's roads - is expanded and drawn as one batch: every contour is drawn, each as it
// would be alone, and no contour reaches into the next - the end of one is not joined to the start of another.
[TestFixture]
[Category("Gpu")]
public class BatchedStrokeRenderTests
{
    private const int Dim = 240;

    // Three open lines one under another, apart, and a closed square on the right.
    private static StreamGeometry ManyContours()
    {
        var g = new StreamGeometry();
        var context = g.Open();
        context.BeginFigure(new Vector2<double>(20, 40), false, false)
            .PolylineLineTo([new Vector2<double>(80, 40), new Vector2<double>(120, 60)], true);
        context.BeginFigure(new Vector2<double>(20, 110), false, false)
            .PolylineLineTo([new Vector2<double>(120, 110)], true);
        context.BeginFigure(new Vector2<double>(20, 180), false, false)
            .PolylineLineTo([new Vector2<double>(60, 200), new Vector2<double>(120, 180)], true);
        context.BeginFigure(new Vector2<double>(160, 60), false, true)
            .PolylineLineTo([new Vector2<double>(220, 60), new Vector2<double>(220, 180), new Vector2<double>(160, 180)], true);
        return g;
    }

    private static byte[] Render(System.Action<Adamantium.UI.Core.Graphics.IDrawingSession> draw)
    {
        var device = GpuTestDevice.Device;
        var factory = new RenderUnitFactory(device, new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(device, factory, Dim, Dim) { ClearColor = Colors.Transparent };

        var content = new TestControl { RenderAction = draw };
        content.Bounds = new Rect(0, 0, Dim, Dim);
        content.RenderSize = new Size<double>(Dim, Dim);

        var root = new VisualRoot(content, Dim, Dim);
        Assert.That(renderer.RenderFrame(root), Is.True, "off-screen frame must render");

        using var img = renderer.RenderTarget.ResolveTexture.ToImage();
        var bytes = new byte[(int)img.TotalSizeInBytes];
        Marshal.Copy(img.DataPointer, bytes, 0, bytes.Length);
        return bytes;
    }

    private static int Alpha(byte[] pixels, int x, int y) => pixels[(y * Dim + x) * 4 + 3];

    [TestCase(PenLineJoin.Miter)]
    [TestCase(PenLineJoin.Bevel)]
    [TestCase(PenLineJoin.Round)]
    public void EveryContour_IsDrawn_AndNoneReachesTheNext(PenLineJoin join)
    {
        var pen = new Pen(new SolidColorBrush(Colors.Black), 8, penLineJoin: join);
        var pixels = Render(s => s.DrawGeometry(null, ManyContours(), pen));

        Assert.Multiple(() =>
        {
            Assert.That(Alpha(pixels, 50, 40), Is.EqualTo(255), "the first line");
            Assert.That(Alpha(pixels, 100, 50), Is.EqualTo(255), "the first line past its corner");
            Assert.That(Alpha(pixels, 70, 110), Is.EqualTo(255), "the second line");
            Assert.That(Alpha(pixels, 90, 190), Is.EqualTo(255), "the third line");
            Assert.That(Alpha(pixels, 190, 60), Is.EqualTo(255), "the square's top");
            Assert.That(Alpha(pixels, 160, 120), Is.EqualTo(255), "the square's closing side");
            Assert.That(Alpha(pixels, 220, 180), Is.EqualTo(255), "the square's corner");

            // Where the end of one contour would be joined to the start of the next.
            Assert.That(Alpha(pixels, 70, 85), Is.Zero, "nothing between the first line's end and the second's start");
            Assert.That(Alpha(pixels, 70, 145), Is.Zero, "nothing between the second line and the third");
            Assert.That(Alpha(pixels, 140, 120), Is.Zero, "nothing between the lines and the square");
            Assert.That(Alpha(pixels, 190, 120), Is.Zero, "the square is a stroke, not a fill");
        });
    }

    // A translucent stroke of many contours still blends once where its own contours cross.
    [Test]
    public void CrossingContoursOfOneStroke_BlendOnce()
    {
        var g = new StreamGeometry();
        var context = g.Open();
        context.BeginFigure(new Vector2<double>(40, 120), false, false)
            .PolylineLineTo([new Vector2<double>(200, 120)], true);
        context.BeginFigure(new Vector2<double>(120, 40), false, false)
            .PolylineLineTo([new Vector2<double>(120, 200)], true);
        var pen = new Pen(new SolidColorBrush(Color.FromRgba(34, 211, 238, 128)), 12);

        var pixels = Render(s => s.DrawGeometry(null, g, pen));

        Assert.Multiple(() =>
        {
            Assert.That(Alpha(pixels, 70, 120), Is.EqualTo(128).Within(3), "one line alone");
            Assert.That(Alpha(pixels, 120, 120), Is.EqualTo(128).Within(3), "the crossing, once");
        });
    }

    private static (Vector2<double>[] Points, bool IsClosed)[] Lines(int count, int pointsEach)
    {
        var lines = new (Vector2<double>[] Points, bool IsClosed)[count];
        for (var k = 0; k < count; k++)
        {
            var points = new Vector2<double>[pointsEach];
            for (var p = 0; p < pointsEach; p++) points[p] = new Vector2<double>(p, k * 10 + (p % 2));
            lines[k] = (points, false);
        }

        return lines;
    }

    private static Adamantium.UI.Rendering.RenderUnits.GpuStrokeRenderComponent Stroke(
        IReadOnlyList<(Vector2<double>[] Points, bool IsClosed)> contours, Pen pen)
    {
        var device = GpuTestDevice.Device;
        return new Adamantium.UI.Rendering.RenderUnits.GpuStrokeRenderComponent(device,
            new Adamantium.UI.FX.UIBasicEffect(device), new Adamantium.UI.FX.StrokeEffect(device), contours, pen,
            new GpuBufferManager(device));
    }

    [Test]
    public void ManyContinuousContours_AreOneDraw()
    {
        using var stroke = Stroke(Lines(500, 20), new Pen(Brushes.Black, 2));

        Assert.Multiple(() =>
        {
            Assert.That(stroke.DrawCount, Is.EqualTo(1));
            Assert.That(stroke.IsBatch(0), Is.True);
        });
    }

    [Test]
    public void DashedContours_KeepADrawEach()
    {
        using var stroke = Stroke(Lines(5, 20), new Pen(Brushes.Black, 2, dashStrokeArray: [4, 2]));

        Assert.That(stroke.DrawCount, Is.EqualTo(5));
    }

    // 4 x 10 000 points: three fit one batch of at most 32 768, the fourth starts the next - alone, so not a batch.
    [Test]
    public void ABatch_StaysWithinItsLimit()
    {
        using var stroke = Stroke(Lines(4, 10_000), new Pen(Brushes.Black, 2));

        Assert.Multiple(() =>
        {
            Assert.That(stroke.DrawCount, Is.EqualTo(2));
            Assert.That(stroke.IsBatch(0), Is.True);
            Assert.That(stroke.IsBatch(1), Is.False);
        });
    }

    [Test]
    public void ABatch_IsUpdatedInPlace_OnlyWhileItsContoursKeepTheirShape()
    {
        var pen = new Pen(Brushes.Black, 2);
        using var stroke = Stroke(Lines(4, 10_000), pen);
        var moved = Lines(4, 10_000);
        moved[2].Points[5] = new Vector2<double>(5, 50);
        var longer = Lines(4, 10_000);
        longer[1] = (Lines(1, 10_001)[0].Points, false);

        Assert.Multiple(() =>
        {
            Assert.That(stroke.TryUpdateGeometry(moved, pen), Is.True, "points moved, contours the same");
            Assert.That(stroke.TryUpdateGeometry(longer, pen), Is.False, "a contour of another length");
        });
    }

    // The unit factory needs one, but nothing here draws a texture or text.
    private sealed class StubResourceFactory : Adamantium.UI.Core.Graphics.IResourceFactory
    {
        public uint MaxTextureDimension => uint.MaxValue;
        public uint MaxTextureArrayLayers => uint.MaxValue;

        public Adamantium.Graphics.Core.ITexture CreateTexture(Adamantium.Graphics.Core.TextureDescription description, byte[] pixelData) => throw new System.NotSupportedException();
        public Adamantium.Graphics.Core.ITexture CreateTextureArray(Adamantium.Graphics.Core.TextureDescription description, System.Collections.Generic.IReadOnlyList<byte[]> layers) => throw new System.NotSupportedException();
        public Adamantium.Graphics.Core.ITexture ImportSharedSurface(Adamantium.Graphics.Core.SharedSurfaceDescriptor descriptor) => throw new System.NotSupportedException();
        public Adamantium.Graphics.Core.IRenderTarget CreateRenderTarget(uint width, uint height, Adamantium.Graphics.Core.MSAALevel msaa, Adamantium.Imaging.SurfaceFormat format, Adamantium.Vulkan.Core.ImageLayout desiredLayout) => throw new System.NotSupportedException();
        public Adamantium.Graphics.Fonts.FontRenderer GetFontRenderer(Adamantium.Graphics.Core.IGraphicsDevice graphicsDevice) => throw new System.NotSupportedException();
    }
}
