using System.Runtime.InteropServices;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Imaging;
using Adamantium.UI.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

// A stroke's brush is painted along the GPU ribbon - a pattern, noise, a gradient laid over the shape's bounds or along
// the line - and its trim window paints nothing when it holds nothing.
[TestFixture]
[Category("Gpu")]
public class StrokeBrushRenderTests
{
    private const int Dim = 240;

    // An open U: across the top, down the right, back along the bottom - so the line's end lies where the bounds start.
    private static StreamGeometry OpenU()
    {
        var g = new StreamGeometry();
        g.Open().BeginFigure(new Vector2<double>(40, 40), false, false).PolylineLineTo(
            [new Vector2<double>(200, 40), new Vector2<double>(200, 200), new Vector2<double>(40, 200)], true);
        return g;
    }

    // A stroke over the same ground twice: a line out and straight back across itself - a cross.
    private static StreamGeometry Cross()
    {
        var g = new StreamGeometry();
        g.Open().BeginFigure(new Vector2<double>(40, 40), false, false).PolylineLineTo(
            [new Vector2<double>(200, 200), new Vector2<double>(200, 40), new Vector2<double>(40, 200)], true);
        return g;
    }

    private static byte[] Render(Pen pen, StreamGeometry geometry = null, IResourceFactory resources = null)
    {
        var device = GpuTestDevice.Device;
        var factory = new RenderUnitFactory(device, resources ?? new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(device, factory, Dim, Dim) { ClearColor = Colors.Transparent };

        geometry ??= OpenU();
        var content = new TestControl { RenderAction = s => s.DrawGeometry(null, geometry, pen) };
        content.Bounds = new Rect(0, 0, Dim, Dim);
        content.RenderSize = new Size<double>(Dim, Dim);

        var root = new VisualRoot(content, Dim, Dim);
        Assert.That(renderer.RenderFrame(root), Is.True, "off-screen frame must render");
        RenderDirty.Clear();

        using var img = renderer.RenderTarget.ResolveTexture.ToImage();
        var bytes = new byte[(int)img.TotalSizeInBytes];
        Marshal.Copy(img.DataPointer, bytes, 0, bytes.Length);
        return bytes;
    }

    private static int Lit(byte[] pixels)
    {
        var count = 0;
        for (var i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] > 0) count++;
        }

        return count;
    }

    private static (int R, int B, int A) At(byte[] pixels, int x, int y)
    {
        var o = (y * Dim + x) * 4;
        return (pixels[o + 2], pixels[o], pixels[o + 3]);   // the target is BGRA
    }

    private static GradientStopCollection RedToBlue() =>
        new([new GradientStop(new Color(255, 0, 0), 0), new GradientStop(new Color(0, 0, 255), 1)]);

    [TestCase(0.5, 0.5)]
    [TestCase(0.7, 0.3)]
    public void AnEmptyTrimWindow_DrawsNothing(double trimStart, double trimEnd)
    {
        // A progress ring at nought: round ends on a window that holds nothing must not leave a dot behind.
        var pen = new Pen(Brushes.White, 12, startLineCap: PenLineCap.ConvexRound, endLineCap: PenLineCap.ConvexRound,
            trimStart: trimStart, trimEnd: trimEnd);

        Assert.That(Lit(Render(pen)), Is.Zero);
    }

    [TestCase(PenBrushMapping.Bounds)]
    [TestCase(PenBrushMapping.Along)]
    public void APatternStroke_IsPainted(PenBrushMapping mapping)
    {
        var brush = new PatternBrush { Color1 = Colors.White, Color2 = Colors.White, CellSize = 4 };
        var pixels = Render(new Pen(brush, 12) { BrushMapping = mapping });

        Assert.That(At(pixels, 120, 40).A, Is.EqualTo(255), "on the line");
    }

    [Test]
    public void ANoiseStroke_IsPainted()
    {
        var brush = new NoiseBrush { Color1 = Colors.White, Color2 = Colors.White };
        var pixels = Render(new Pen(brush, 12));

        Assert.That(At(pixels, 200, 120).A, Is.EqualTo(255), "on the line");
    }

    [Test]
    public void AGradientAlongTheLine_RunsFromItsStartToItsEnd()
    {
        // Left to right over the bounds; from the first point to the last along the line. Near the line's END, at the
        // bottom left, the two disagree: red over the bounds, blue along the line.
        LinearGradientBrush Horizontal() => new(RedToBlue())
        {
            StartPoint = new Vector2<double>(0, 0),
            EndPoint = new Vector2<double>(1, 0),
        };

        var overBounds = At(Render(new Pen(Horizontal(), 12)), 50, 200);
        var along = At(Render(new Pen(Horizontal(), 12) { BrushMapping = PenBrushMapping.Along }), 50, 200);

        Assert.Multiple(() =>
        {
            Assert.That(overBounds.R, Is.GreaterThan(overBounds.B), "over the bounds the left edge is the start");
            Assert.That(along.B, Is.GreaterThan(along.R), "along the line the bottom left is the end");
        });
    }

    [Test]
    public void AStrokeOfALayer_ShowsThatLayer()
    {
        const int side = 16;
        static byte[] Flat(byte r, byte g, byte b)
        {
            var pixels = new byte[side * side * 4];
            for (var i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = b;
                pixels[i + 1] = g;
                pixels[i + 2] = r;
                pixels[i + 3] = 255;
            }

            return pixels;
        }

        using var atlas = new TextureArrayImage(side, side, 8, SurfaceFormat.B8G8R8A8.UNorm);
        var red = atlas.TakeLayer();
        var green = atlas.TakeLayer();
        atlas.Write(red, Flat(255, 0, 0));
        atlas.Write(green, Flat(0, 255, 0));

        var pixels = Render(new Pen(new ImageBrush { Source = green }, 12), resources: new DeviceResourceFactory(GpuTestDevice.Device));
        var o = (40 * Dim + 120) * 4;

        Assert.That((pixels[o + 2], pixels[o + 1], pixels[o]), Is.EqualTo(((byte)0, (byte)255, (byte)0)), "the second layer, on the line");
    }

    [Test]
    public void ATranslucentCross_InOnePass_ShowsItsCrossing_InTwo_DoesNot()
    {
        // Half-alpha white: the crossing is laid on twice in one pass and comes out more opaque than the arms; two
        // passes paint every pixel once, so the crossing matches them.
        var brush = new SolidColorBrush(new Color(255, 255, 255, 128));
        var blend = Render(new Pen(brush, 12) { Overlap = PenOverlap.Blend }, Cross());
        var union = Render(new Pen(brush, 12) { Overlap = PenOverlap.Union }, Cross());

        Assert.Multiple(() =>
        {
            Assert.That(At(blend, 120, 120).A, Is.GreaterThan(At(blend, 80, 80).A + 20), "one pass: the crossing is denser");
            Assert.That(At(union, 120, 120).A, Is.EqualTo(At(union, 80, 80).A).Within(2), "two passes: the crossing is as the arm");
        });
    }

    [Test]
    public void APictureStroke_TakesOnePass_WhenThePenSaysSo()
    {
        // A picture is never known to be opaque, so by the brush it takes two passes; told one, its crossing is laid on twice.
        using var atlas = new TextureArrayImage(16, 16, 1, SurfaceFormat.B8G8R8A8.UNorm);
        var layer = atlas.TakeLayer();
        var half = new byte[16 * 16 * 4];
        for (var i = 0; i < half.Length; i += 4) { half[i] = 128; half[i + 1] = 128; half[i + 2] = 128; half[i + 3] = 128; }
        atlas.Write(layer, half);
        var resources = new DeviceResourceFactory(GpuTestDevice.Device);

        var auto = Render(new Pen(new ImageBrush { Source = layer }, 12), Cross(), resources);
        var blend = Render(new Pen(new ImageBrush { Source = layer }, 12) { Overlap = PenOverlap.Blend }, Cross(), resources);

        Assert.Multiple(() =>
        {
            Assert.That(At(auto, 80, 80).A, Is.GreaterThan(100), "the arm is painted");
            Assert.That(At(auto, 120, 120).A, Is.EqualTo(At(auto, 80, 80).A).Within(2), "by the brush: two passes");
            Assert.That(At(blend, 120, 120).A, Is.GreaterThan(At(blend, 80, 80).A + 20), "one pass");
        });
    }

    [Test]
    public void ALayerGivenBack_PaintsNothing()
    {
        // Given back, the layer may be taken and written by someone else: a stroke still naming it must not show theirs.
        using var atlas = new TextureArrayImage(16, 16, 4, SurfaceFormat.B8G8R8A8.UNorm);
        var layer = atlas.TakeLayer();
        var white = new byte[16 * 16 * 4];
        System.Array.Fill(white, (byte)255);
        atlas.Write(layer, white);
        atlas.ReleaseLayer(layer);

        var pixels = Render(new Pen(new ImageBrush { Source = layer }, 12), resources: new DeviceResourceFactory(GpuTestDevice.Device));

        Assert.That(Lit(pixels), Is.Zero);
    }

    [Test]
    public void AShapesStrokeProperties_ReachItsPen()
    {
        var shape = new Adamantium.UI.Controls.Shapes.Rectangle
        {
            Stroke = Brushes.White,
            StrokeThickness = 2,
            StrokeOverlap = PenOverlap.Blend,
            StrokeBrushMapping = PenBrushMapping.Along,
        };

        var pen = shape.GetPen();

        Assert.Multiple(() =>
        {
            Assert.That(pen.Overlap, Is.EqualTo(PenOverlap.Blend));
            Assert.That(pen.BrushMapping, Is.EqualTo(PenBrushMapping.Along));
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
