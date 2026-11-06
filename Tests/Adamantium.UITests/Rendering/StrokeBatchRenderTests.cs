using System;
using System.Runtime.InteropServices;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

// Strokes that are all their unit draws go as one batch - a record each, not an op each - and draw what each would have
// drawn alone: dashes, trims, caps and joins, opaque and translucent, and after a pen that changed with no walk.
[TestFixture]
[Category("Gpu")]
public class StrokeBatchRenderTests
{
    private const int Dim = 200;

    private sealed class Scene : IDisposable
    {
        public OffscreenTestRenderer Renderer;
        public VisualRoot Root;
        public TestControl Content;

        public byte[] Draw()
        {
            Assert.That(Renderer.RenderFrame(Root), Is.True, "off-screen frame must render");
            RenderDirty.Clear();
            using var img = Renderer.RenderTarget.ResolveTexture.ToImage();
            var bytes = new byte[(int)img.TotalSizeInBytes];
            Marshal.Copy(img.DataPointer, bytes, 0, bytes.Length);
            return bytes;
        }

        public void Dispose() => Renderer.Dispose();
    }

    private static Scene NewScene(Action<IDrawingSession> draw)
    {
        var device = GpuTestDevice.Device;
        var renderer = new OffscreenTestRenderer(device, new RenderUnitFactory(device, new StubResourceFactory()), Dim, Dim)
        {
            ClearColor = Colors.Black
        };

        var content = new TestControl { RenderAction = draw };
        content.Bounds = new Rect(0, 0, Dim, Dim);
        content.RenderSize = new Size<double>(Dim, Dim);
        return new Scene { Renderer = renderer, Root = new VisualRoot(content, Dim, Dim), Content = content };
    }

    private static byte[] DrawOnce(Action<IDrawingSession> draw, bool batched)
    {
        var was = StrokeBatchCollector.Enabled;
        StrokeBatchCollector.Enabled = batched;
        try
        {
            using var scene = NewScene(draw);
            return scene.Draw();
        }
        finally
        {
            StrokeBatchCollector.Enabled = was;
        }
    }

    // Pixels whose channels differ by more than the last bit of an antialiased edge: the batch works the width out from
    // the matrix on the GPU, a lone stroke on the CPU, and the two may round a feather's last bit apart.
    private static int Differing(byte[] a, byte[] b)
    {
        var count = 0;
        for (var i = 0; i < a.Length; i += 4)
        {
            for (var c = 0; c < 4; c++)
            {
                if (Math.Abs(a[i + c] - b[i + c]) <= 2) continue;
                count++;
                break;
            }
        }

        return count;
    }

    private static int Lit(byte[] pixels)
    {
        var count = 0;
        for (var i = 0; i < pixels.Length; i += 4)
            if (pixels[i] > 0 || pixels[i + 1] > 0 || pixels[i + 2] > 0) count++;
        return count;
    }

    // A fan of lines in every style the pen panel offers: dashes with every cap, a trim window, a round and a bevel join,
    // opaque and translucent.
    private static void Fan(IDrawingSession s)
    {
        var caps = new[] { PenLineCap.Flat, PenLineCap.Square, PenLineCap.ConvexRound, PenLineCap.ConvexTriangle,
            PenLineCap.ConcaveTriangle, PenLineCap.ConcaveRound };
        for (var i = 0; i < 12; i++)
        {
            var color = i % 3 == 0 ? Color.FromRgba(255, 136, 0, 255) : i % 3 == 1 ? Color.FromRgba(34, 211, 238, 160)
                : Color.FromRgba(160, 255, 120, 255);
            var cap = caps[i % caps.Length];
            var pen = new Pen(new SolidColorBrush(color), 3 + i % 4, dashStrokeArray: i % 2 == 0 ? [12.0, 6.0] : null,
                startLineCap: cap, endLineCap: cap, penLineJoin: i % 4 < 2 ? PenLineJoin.Round : PenLineJoin.Bevel,
                trimStart: i % 5 == 0 ? 0.1 : 0.0, trimEnd: i % 5 == 0 ? 0.85 : 1.0, dashStartCap: cap, dashEndCap: cap);
            var g = new StreamGeometry();
            g.Open().BeginFigure(new Vector2<double>(10, 10 + i * 15), false, false).PolylineLineTo(
                [new Vector2<double>(90, 30 + i * 12), new Vector2<double>(150, 5 + i * 15), new Vector2<double>(190, 40 + i * 13)],
                true);
            s.DrawGeometry(null, g, pen);
        }
    }

    [Test]
    public void ManyStrokesOfOneElement_AreRecordsOfOneBatch_NotAnOpEach()
    {
        using var scene = NewScene(s =>
        {
            var pen = new Pen(Brushes.White, 2);
            for (var i = 0; i < 60; i++)
                s.DrawGeometry(null, new LineGeometry(new Vector2<double>(5, 3 + i * 3), new Vector2<double>(195, 3 + i * 3)), pen);
        });

        scene.Draw();

        Assert.That(scene.Renderer.Cache.RecordedOpCount, Is.LessThan(10), "sixty lines are one batch, not sixty ops");
    }

    [Test]
    public void TheBatch_DrawsWhatEachStrokeDrawsAlone()
    {
        var alone = DrawOnce(Fan, batched: false);
        var batched = DrawOnce(Fan, batched: true);

        Assert.Multiple(() =>
        {
            Assert.That(Lit(alone), Is.GreaterThan(2000), "the fan must actually have drawn");
            Assert.That(Differing(alone, batched), Is.Zero, "the batch must draw what each stroke draws alone");
        });
    }

    [Test]
    public void APenThatChanges_IsDrawnAsChanged()
    {
        // Thickness and color change without a new stroke (TryRepoint): the record is described again, not left behind.
        var pen = new Pen(Brushes.White, 2);
        using var scene = NewScene(s => s.DrawGeometry(null, new LineGeometry(new Vector2<double>(20, 100), new Vector2<double>(180, 100)), pen));
        var line = scene.Content;
        scene.Draw();

        pen = new Pen(new SolidColorBrush(Color.FromRgba(255, 0, 0, 255)), 10);
        line.InvalidateRender(false);
        var changed = scene.Draw();

        var fresh = DrawOnce(s => s.DrawGeometry(null, new LineGeometry(new Vector2<double>(20, 100), new Vector2<double>(180, 100)),
            new Pen(new SolidColorBrush(Color.FromRgba(255, 0, 0, 255)), 10)), batched: true);
        Assert.That(Differing(changed, fresh), Is.Zero, "the changed pen must be drawn as a fresh one is");
    }

    [Test]
    public void APenThatNeedsANewStroke_IsDrawnByIt_NotByTheOldOne()
    {
        // Solid to dashed builds a new stroke: the records must not go on describing the one it replaced.
        var pen = new Pen(Brushes.White, 6);
        using var scene = NewScene(s => s.DrawGeometry(null, new LineGeometry(new Vector2<double>(20, 100), new Vector2<double>(180, 100)), pen));
        scene.Draw();

        pen = new Pen(Brushes.White, 6, dashStrokeArray: [10.0, 10.0], penLineJoin: PenLineJoin.Round);
        scene.Content.InvalidateRender(false);
        var changed = scene.Draw();
        changed = scene.Draw();   // and the frame after, once the old stroke is gone

        var fresh = DrawOnce(s => s.DrawGeometry(null, new LineGeometry(new Vector2<double>(20, 100), new Vector2<double>(180, 100)),
            new Pen(Brushes.White, 6, dashStrokeArray: [10.0, 10.0], penLineJoin: PenLineJoin.Round)), batched: true);
        Assert.That(Differing(changed, fresh), Is.Zero, "the new stroke must be drawn, dashes and all");
    }

    [Test]
    public void ADrawingsOwnTransform_PlacesItsStroke()
    {
        // DrawGeometry with a transform - an icon's outline: the batch draws it where the transform puts it.
        var transform = Matrix4x4F.Identity;
        transform.M11 = 2f;
        transform.M22 = 2f;
        transform.M41 = 30f;
        transform.M42 = 20f;
        void Draw(IDrawingSession s) =>
            s.DrawGeometry(null, new LineGeometry(new Vector2<double>(0, 10), new Vector2<double>(60, 10)), new Pen(Brushes.White, 3), transform);

        var alone = DrawOnce(Draw, batched: false);
        var batched = DrawOnce(Draw, batched: true);

        Assert.Multiple(() =>
        {
            Assert.That(Lit(alone), Is.GreaterThan(300), "the line must actually have drawn");
            Assert.That(Differing(alone, batched), Is.Zero, "the batch must place it as the stroke alone does");
        });
    }

    [Test]
    public void UnderAFadingParent_AnOpaqueStrokeStillBlendsItsCrossingOnce()
    {
        // The parent's opacity comes from its slot, not from the stroke's color: the stroke still has to take the union.
        var g = new StreamGeometry();
        g.Open().BeginFigure(new Vector2<double>(20, 20), false, false).PolylineLineTo(
            [new Vector2<double>(180, 180), new Vector2<double>(20, 180), new Vector2<double>(180, 20)], true);

        var device = GpuTestDevice.Device;
        using var renderer = new OffscreenTestRenderer(device, new RenderUnitFactory(device, new StubResourceFactory()), Dim, Dim)
        {
            ClearColor = Colors.Transparent
        };
        var parent = new TestControl { Bounds = new Rect(0, 0, Dim, Dim), RenderSize = new Size<double>(Dim, Dim), Opacity = 0.5 };
        var child = new TestControl
        {
            Bounds = new Rect(0, 0, Dim, Dim), RenderSize = new Size<double>(Dim, Dim),
            RenderAction = s => s.DrawGeometry(null, g, new Pen(Brushes.White, 12)),
        };
        parent.Add(child);
        Assert.That(renderer.RenderFrame(new VisualRoot(parent, Dim, Dim)), Is.True);
        using var img = renderer.RenderTarget.ResolveTexture.ToImage();
        var pixels = new byte[(int)img.TotalSizeInBytes];
        Marshal.Copy(img.DataPointer, pixels, 0, pixels.Length);

        int Alpha(int x, int y) => pixels[(y * Dim + x) * 4 + 3];
        Assert.That(Alpha(100, 100), Is.EqualTo(Alpha(60, 60)).Within(3), "the crossing is as the arm, not darker");
    }

    [Test]
    public void MoreStrokesThanTheFirstBufferHolds_KeepTheirOrderOverWhatIsUnderThem()
    {
        // Past the first buffer's 256 records the frame is held back and drawn whole by the next walk - and what is drawn
        // must keep a background under the lines, as the strokes alone do.
        void Draw(IDrawingSession s)
        {
            s.DrawRectangle(new SolidColorBrush(Color.FromRgba(40, 60, 200, 255)), new Rect(0, 0, Dim, Dim));
            var pen = new Pen(Brushes.White, 1);
            for (var i = 0; i < 300; i++)
                s.DrawGeometry(null, new LineGeometry(new Vector2<double>(5, 2 + i * 0.6), new Vector2<double>(195, 2 + i * 0.6)), pen);
        }

        var alone = DrawOnce(Draw, batched: false);
        var was = StrokeBatchCollector.Enabled;
        StrokeBatchCollector.Enabled = true;
        byte[] batched;
        try
        {
            using var scene = NewScene(Draw);
            scene.Draw();
            scene.Content.InvalidateRender(false);
            batched = scene.Draw();
        }
        finally
        {
            StrokeBatchCollector.Enabled = was;
        }

        Assert.That(Differing(alone, batched), Is.Zero, "every line over the background, as drawn alone");
    }

    // The unit factory needs one, but nothing here draws a texture or text.
    private sealed class StubResourceFactory : IResourceFactory
    {
        public uint MaxTextureDimension => uint.MaxValue;
        public uint MaxTextureArrayLayers => uint.MaxValue;

        public Graphics.Core.ITexture CreateTexture(Graphics.Core.TextureDescription description, byte[] pixelData) => throw new NotSupportedException();
        public Graphics.Core.ITexture CreateTextureArray(Graphics.Core.TextureDescription description, System.Collections.Generic.IReadOnlyList<byte[]> layers) => throw new NotSupportedException();
        public Graphics.Core.ITexture ImportSharedSurface(Graphics.Core.SharedSurfaceDescriptor descriptor) => throw new NotSupportedException();
        public Graphics.Core.IRenderTarget CreateRenderTarget(uint width, uint height, Graphics.Core.MSAALevel msaa, Imaging.SurfaceFormat format, Vulkan.Core.ImageLayout desiredLayout) => throw new NotSupportedException();
        public Graphics.Fonts.FontRenderer GetFontRenderer(Graphics.Core.IGraphicsDevice graphicsDevice) => throw new NotSupportedException();
    }
}
