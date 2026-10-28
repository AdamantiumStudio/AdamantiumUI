using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.Graphics.Core;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry.Shapes;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Rendering;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

// A control whose count of shapes changes re-issues the layer they share with a neighbor, moving the neighbor's shapes
// with it; the neighbor's next patch has to write where they are now. The first growth carries the layer to the end of
// the arena, the second shifts what follows it in the room the move left. Checked against a full walk.
[TestFixture]
[Category("Gpu")]
public class ReissuedLayerNeighborRenderTests
{
    private const int Dim = 64;
    private const int Dot = 8;

    private sealed class Scene : IDisposable
    {
        public OffscreenTestRenderer Renderer;
        public VisualRoot Root;
        public TestControl Growing;
        public TestControl Neighbor;

        public void Draw()
        {
            Assert.That(Renderer.RenderFrame(Root), Is.True, "off-screen frame must render");
            RenderDirty.Clear();
        }

        public void Dispose() => Renderer.Dispose();
    }

    private static TestControl Placed(Rect bounds) =>
        new() { Bounds = bounds, RenderSize = new Size<double>(bounds.Width, bounds.Height) };

    private static Brush Gradient(Color from) => new LinearGradientBrush
    {
        GradientStops = { new GradientStop(from, 0), new GradientStop(Colors.Blue, 1) }
    };

    private static Action<IDrawingSession> Dots(int count, Brush brush) => s =>
    {
        for (var i = 0; i < count; i++)
        {
            s.DrawEllipse(new Rect(i * (Dot + 2), 0, Dot, Dot), brush, 0, 360, default(EllipseType));
        }
    };

    private static Action<IDrawingSession> GradientSquares(int count) => s =>
    {
        for (var i = 0; i < count; i++)
        {
            s.DrawRectangle(Gradient(Colors.White), new Rect(i * (Dot + 2), 0, Dot, Dot));
        }
    };

    // A dot, then a gradient square: one group in two arenas, filed under the dots'.
    private static Action<IDrawingSession> DotAndSquare(Brush dot, Brush square) => s =>
    {
        s.DrawEllipse(new Rect(0, 0, Dot, Dot), dot, 0, 360, default(EllipseType));
        s.DrawRectangle(square, new Rect(20, 0, Dot * 2, Dot * 2));
    };

    private static Scene NewScene(Action<IDrawingSession> growing, Action<IDrawingSession> neighbor)
    {
        var device = GpuTestDevice.Device;
        var factory = new RenderUnitFactory(device, new StubResourceFactory());
        var renderer = new OffscreenTestRenderer(device, factory, Dim, Dim) { ClearColor = Colors.Black };

        var stage = Placed(new Rect(0, 0, Dim, Dim));
        var grows = Placed(new Rect(0, 0, Dim, Dot));
        grows.RenderAction = growing;
        stage.Add(grows);
        var next = Placed(new Rect(0, Dot * 2, Dim, Dot * 3));
        next.RenderAction = neighbor;
        stage.Add(next);

        var scene = new Scene { Renderer = renderer, Root = new VisualRoot(stage, Dim, Dim), Growing = grows, Neighbor = next };
        scene.Draw();
        return scene;
    }

    private static void Grow(Scene scene, Func<int, Action<IDrawingSession>> shapes, int[] counts)
    {
        foreach (var count in counts)
        {
            scene.Growing.RenderAction = shapes(count);
            scene.Growing.Invalidate();
            scene.Draw();
            Assert.That(scene.Renderer.Cache.LastFrameReplayed, Is.True, "the grown control must be spliced, not walked");
        }
    }

    private static void Redraw(Scene scene, Action<IDrawingSession> neighbor)
    {
        scene.Neighbor.RenderAction = neighbor;
        scene.Neighbor.Invalidate();
        scene.Draw();
        Assert.That(scene.Renderer.Cache.LastFrameReplayed, Is.True, "a recolor must patch, not walk");
    }

    private static byte[] Pixels(OffscreenTestRenderer renderer)
    {
        using var img = renderer.RenderTarget.ResolveTexture.ReadbackToImage();
        var bytes = new byte[(int)img.TotalSizeInBytes];
        Marshal.Copy(img.DataPointer, bytes, 0, bytes.Length);
        return bytes;
    }

    private static int DifferingPixels(byte[] a, byte[] b)
    {
        var count = 0;
        for (var i = 0; i < a.Length; i += 4)
        {
            if (a[i] != b[i] || a[i + 1] != b[i + 1] || a[i + 2] != b[i + 2] || a[i + 3] != b[i + 3]) count++;
        }

        return count;
    }

    private static void AssertMatchesAFullWalk(Scene scene, string because)
    {
        var patched = Pixels(scene.Renderer);
        RenderDirty.MarkStructural();
        scene.Draw();

        Assert.That(scene.Renderer.Cache.LastFrameReplayed, Is.False, "the reference frame has to actually walk");
        Assert.That(DifferingPixels(patched, Pixels(scene.Renderer)), Is.Zero, because);
    }

    [TestCase(2)]
    [TestCase(2, 3)]
    public void AShapeCountChange_LeavesTheNeighborsShapesWhereItsPatchWrites(params int[] counts)
    {
        using var scene = NewScene(Dots(1, Brushes.White), Dots(2, Brushes.Red));
        Grow(scene, count => Dots(count, Brushes.White), counts);

        Redraw(scene, Dots(2, Brushes.Green));
        AssertMatchesAFullWalk(scene, "the neighbor's dots must be recolored where they are now");
    }

    // A gradient polygon shares the gradient squares' arena without being one of them.
    [TestCase(2)]
    [TestCase(2, 3)]
    public void AGrownLayer_LeavesAGradientPolygonWhereItsPatchWrites(params int[] counts)
    {
        Action<IDrawingSession> Polygon(Color color) => s => s.DrawRegularPolygon(Gradient(color), new Rect(0, 0, Dot * 2, Dot * 2), 5);
        using var scene = NewScene(GradientSquares(1), Polygon(Colors.Red));
        Grow(scene, GradientSquares, counts);

        Redraw(scene, Polygon(Colors.Green));
        AssertMatchesAFullWalk(scene, "the polygon must be recolored where it is now");
    }

    // The neighbor's dot moved with the layer; its square, in another arena, did not, and both stay patchable.
    [TestCase(2)]
    [TestCase(2, 3)]
    public void AGrownLayer_LeavesBothArenasOfANeighborPatchable(params int[] counts)
    {
        using var scene = NewScene(Dots(1, Brushes.White), DotAndSquare(Brushes.Red, Gradient(Colors.Yellow)));
        Grow(scene, count => Dots(count, Brushes.White), counts);

        Redraw(scene, DotAndSquare(Brushes.Green, Gradient(Colors.Lime)));
        AssertMatchesAFullWalk(scene, "the neighbor's dot and square must be recolored where they are");
    }

    // A brush recolored in place repaints whoever paints with it, through the records the move left.
    [TestCase(2, true)]
    [TestCase(2, false)]
    public void AGrownLayer_LeavesANeighborsBrushesRepaintable(int count, bool square)
    {
        var dot = new SolidColorBrush(Colors.Red);
        var stop = new GradientStop(Colors.Yellow, 0);
        using var scene = NewScene(Dots(1, Brushes.White),
            DotAndSquare(dot, new LinearGradientBrush { GradientStops = { stop, new GradientStop(Colors.Blue, 1) } }));
        Grow(scene, n => Dots(n, Brushes.White), [count]);

        if (square)
        {
            stop.Color = Colors.Lime;
        }
        else
        {
            dot.Color = Colors.Lime;
        }

        scene.Draw();
        AssertMatchesAFullWalk(scene, "the repainted brush must show where its shape is now");
    }

    // The unit factory needs one, but nothing here draws a texture or text.
    private sealed class StubResourceFactory : IResourceFactory
    {
        public uint MaxTextureDimension => uint.MaxValue;
        public uint MaxTextureArrayLayers => uint.MaxValue;

        public ITexture CreateTexture(TextureDescription description, byte[] pixelData) => throw new NotSupportedException();
        public ITexture CreateTextureArray(TextureDescription description, IReadOnlyList<byte[]> layers) => throw new NotSupportedException();
        public ITexture ImportSharedSurface(SharedSurfaceDescriptor descriptor) => throw new NotSupportedException();
        public IRenderTarget CreateRenderTarget(uint width, uint height, MSAALevel msaa, SurfaceFormat format, ImageLayout desiredLayout)
            => throw new NotSupportedException();
        public Adamantium.Graphics.Fonts.FontRenderer GetFontRenderer(IGraphicsDevice graphicsDevice) => throw new NotSupportedException();
    }
}
