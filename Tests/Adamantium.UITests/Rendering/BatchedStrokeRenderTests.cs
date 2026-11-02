using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

// A stroke of many contours - a map's roads - is drawn as one batch: every contour is drawn, each as it
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

    // Dashed contours - a map's paths and rail ticks - are one batch too, each still cut by its own length.
    [Test]
    public void DashedContours_AreOneDraw()
    {
        using var stroke = Stroke(Lines(500, 20), new Pen(Brushes.Black, 2, dashStrokeArray: [4, 2]));

        Assert.Multiple(() =>
        {
            Assert.That(stroke.DrawCount, Is.EqualTo(1));
            Assert.That(stroke.IsBatch(0), Is.True);
        });
    }

    // Dashes fitted to a closed contour stretch to that contour's length, so each keeps a draw of its own.
    [Test]
    public void FittedDashes_KeepADrawEach()
    {
        var squares = new (Vector2<double>[] Points, bool IsClosed)[5];
        for (var k = 0; k < squares.Length; k++)
            squares[k] = ([new(0, k * 20), new(10, k * 20), new(10, k * 20 + 10), new(0, k * 20 + 10)], true);

        using var stroke = Stroke(squares,
            new Pen(Brushes.Black, 2, dashStrokeArray: [4, 2], fitDashesToContour: true));

        Assert.That(stroke.DrawCount, Is.EqualTo(5));
    }

    // Translucent dashes of two contours of a batch blend once where they cross, as one stroke's do.
    [Test]
    public void CrossingDashedContoursOfOneStroke_BlendOnce()
    {
        var g = new StreamGeometry();
        var context = g.Open();
        context.BeginFigure(new Vector2<double>(40, 120), false, false)
            .PolylineLineTo([new Vector2<double>(200, 120)], true);
        context.BeginFigure(new Vector2<double>(120, 40), false, false)
            .PolylineLineTo([new Vector2<double>(120, 200)], true);
        var pen = new Pen(new SolidColorBrush(Color.FromRgba(34, 211, 238, 128)), 12, dashStrokeArray: [200, 10]);

        var pixels = Render(s => s.DrawGeometry(null, g, pen));

        Assert.Multiple(() =>
        {
            Assert.That(Alpha(pixels, 70, 120), Is.EqualTo(128).Within(3), "one dash alone");
            Assert.That(Alpha(pixels, 120, 120), Is.EqualTo(128).Within(3), "the crossing, once");
        });
    }

    // In a batch every contour starts its dashes at its own start, and nothing of one reaches the next.
    [Test]
    public void DashedContoursOfABatch_AreEachCutFromTheirOwnStart()
    {
        var pen = new Pen(new SolidColorBrush(Colors.Black), 8, dashStrokeArray: [20, 20],
            dashStartCap: PenLineCap.Flat, dashEndCap: PenLineCap.Flat);
        var pixels = Render(s => s.DrawGeometry(null, ManyContours(), pen));

        Assert.Multiple(() =>
        {
            Assert.That(Alpha(pixels, 30, 40), Is.EqualTo(255), "the first line's first dash");
            Assert.That(Alpha(pixels, 50, 40), Is.Zero, "the first line's first gap");
            Assert.That(Alpha(pixels, 70, 40), Is.EqualTo(255), "the first line's second dash");
            Assert.That(Alpha(pixels, 30, 110), Is.EqualTo(255), "the second line's first dash");
            Assert.That(Alpha(pixels, 50, 110), Is.Zero, "the second line's first gap");
            Assert.That(Alpha(pixels, 70, 85), Is.Zero, "nothing between the first line and the second");
            Assert.That(Alpha(pixels, 140, 120), Is.Zero, "nothing between the lines and the square");
        });
    }

    // Flat-ended dashes are a mask along the line; an odd pattern is taken twice, its runs on and off in turn - 20 on,
    // 10 off, 10 on, then 20 off, 10 on, 10 off.
    [Test]
    public void AnOddPatternOfFlatDashes_IsTakenTwice()
    {
        var g = new StreamGeometry();
        g.Open().BeginFigure(new Vector2<double>(20, 120), false, false)
            .PolylineLineTo([new Vector2<double>(220, 120)], true);
        var pen = new Pen(new SolidColorBrush(Colors.Black), 8, dashStrokeArray: [20, 10, 10],
            dashStartCap: PenLineCap.Flat, dashEndCap: PenLineCap.Flat);

        var pixels = Render(s => s.DrawGeometry(null, g, pen));

        Assert.Multiple(() =>
        {
            Assert.That(Alpha(pixels, 25, 120), Is.EqualTo(255), "the first dash");
            Assert.That(Alpha(pixels, 45, 120), Is.Zero, "the first gap");
            Assert.That(Alpha(pixels, 55, 120), Is.EqualTo(255), "the second dash");
            Assert.That(Alpha(pixels, 70, 120), Is.Zero, "the second pass's 20, off");
            Assert.That(Alpha(pixels, 85, 120), Is.EqualTo(255), "its first 10, on");
            Assert.That(Alpha(pixels, 95, 120), Is.Zero, "its last 10, off");
            Assert.That(Alpha(pixels, 105, 120), Is.EqualTo(255), "the pattern again");
        });
    }

    // A right turn at (120, 120), 10 wide, round joined: the join's outer side round (123, 117) is in neither segment.
    private static (StreamGeometry Geometry, Func<double[], Pen> Pen) Corner()
    {
        var g = new StreamGeometry();
        g.Open().BeginFigure(new Vector2<double>(20, 120), false, false)
            .PolylineLineTo([new Vector2<double>(120, 120), new Vector2<double>(120, 220)], true);
        return (g, dashes => new Pen(new SolidColorBrush(Colors.Black), 10, dashStrokeArray: dashes,
            penLineJoin: PenLineJoin.Round, dashStartCap: PenLineCap.Flat, dashEndCap: PenLineCap.Flat));
    }

    // The corner falls in a gap, the next dash starting a unit past it: no join, so nothing in the gap.
    [Test]
    public void FlatDashes_AreNotJoinedAtACornerInAGap()
    {
        var (g, pen) = Corner();

        var pixels = Render(s => s.DrawGeometry(null, g, pen([90, 11])));

        Assert.Multiple(() =>
        {
            Assert.That(Alpha(pixels, 123, 117), Is.Zero, "the join's outer side");
            Assert.That(Alpha(pixels, 112, 120), Is.Zero, "the gap before the corner");
            Assert.That(Alpha(pixels, 120, 130), Is.EqualTo(255), "the next dash");
        });
    }

    [Test]
    public void FlatDashes_AreJoinedAtACornerADashRunsThrough()
    {
        var (g, pen) = Corner();

        var pixels = Render(s => s.DrawGeometry(null, g, pen([150, 10])));

        Assert.That(Alpha(pixels, 123, 117), Is.EqualTo(255));
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
