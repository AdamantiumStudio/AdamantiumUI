using System;
using System.Runtime.InteropServices;
using Adamantium.Graphics.Core;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Animation;
using Adamantium.UI.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

// A reflected element is drawn whole into its layer, in the layer's own region: what a scroll or the window's edge hides
// of it is still mirrored into view - on a walked frame and on a replayed one alike.
[TestFixture]
[Category("Gpu")]
public class WholeLayerReflectionRenderTests
{
    private const int W = 64;
    private const int H = 128;

    [TearDown]
    public void TearDown() => AnimationManager.Reset();

    private static TestControl Placed(Rect bounds) =>
        new() { Bounds = bounds, RenderSize = new Size(bounds.Width, bounds.Height) };

    // Red over lime, 40x40, mirrored whole with no fade.
    private static TestControl RedLime(Rect bounds)
    {
        var element = Placed(bounds);
        element.Reflection = new Reflection { Size = 1, Opacity = 1, Fade = 0 };
        element.RenderAction = s => s
            .DrawRectangle(Brushes.Red, new Rect(0, 0, 40, 20))
            .DrawRectangle(Brushes.Lime, new Rect(0, 20, 40, 20));
        return element;
    }

    private static OffscreenTestRenderer NewRenderer()
    {
        var device = GpuTestDevice.Device;
        return new OffscreenTestRenderer(device, new RenderUnitFactory(device, new StubResourceFactory()), W, H)
        {
            ClearColor = Colors.Blue
        };
    }

    private static void Draw(OffscreenTestRenderer renderer, IRootVisualComponent root)
    {
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();
    }

    private static byte[] Pixels(OffscreenTestRenderer renderer)
    {
        using var image = renderer.RenderTarget.ResolveTexture.ReadbackToImage();
        var bytes = new byte[(int)image.TotalSizeInBytes];
        Marshal.Copy(image.DataPointer, bytes, 0, bytes.Length);
        return bytes;
    }

    // BGRA.
    private static int Red(byte[] pixels, int x, int y) => pixels[(y * W + x) * 4 + 2];

    [Test]
    public void AScrollReplayed_KeepsTheReflectionWhole()
    {
        using var renderer = NewRenderer();
        var stage = Placed(new Rect(0, 0, W, H));
        var viewport = Placed(new Rect(0, 20, W, 108));
        viewport.ClipToBounds = true;
        var scrolled = Placed(new Rect(0, 0, W, 200));
        scrolled.IsRenderMotionNode = true;
        scrolled.Add(RedLime(new Rect(4, 10, 40, 40)));
        viewport.Add(scrolled);
        stage.Add(viewport);
        var root = new VisualRoot(stage, W, H);
        Draw(renderer, root);
        Draw(renderer, root);

        scrolled.Bounds = new Rect(0, -20, W, 200);
        Draw(renderer, root);

        // The element at rows 10 to 50, the viewport hiding it above 20, the floor at 50: rows 10 to 20 mirrored at 80
        // to 90.
        Assert.Multiple(() =>
        {
            Assert.That(renderer.Cache.LastFrameReplayed, Is.True, "the move is replayed");
            Assert.That(Red(Pixels(renderer), 24, 85), Is.GreaterThan(200), "the hidden red, mirrored");
        });
    }

    // What the viewport hides of the element's glow is mirrored too - drawn with it, or, soft-edged, in a texture of its
    // own; walked, or replayed.
    [TestCase(0, 0.0, 1)]
    [TestCase(0, 0.0, 3)]
    [TestCase(0, 1.0, 3)]
    [TestCase(4, 0.0, 1)]
    [TestCase(4, 0.0, 3)]
    [TestCase(4, 1.0, 3)]
    public void AnElementsGlow_IsWholeInItsReflectionUnderAViewport(double soft, double turbulence, int frames)
    {
        // The element at rows 10 to 50, its glow to 56 and the floor there: the reflection lies from 56 down whether a
        // viewport hides the element above row 20 or not, and the glow beside it, two pixels out, is the same in both.
        var cut = GlowBeside(20, soft, turbulence, frames);
        var whole = GlowBeside(0, soft, turbulence, frames);

        Assert.Multiple(() =>
        {
            for (var row = 58; row < 106; row += 4)
            {
                Assert.That(cut[row], Is.EqualTo(whole[row]).Within(8), $"row {row}");
            }
        });
    }

    private static int[] GlowBeside(int viewportTop, double soft, double turbulence, int frames)
    {
        using var renderer = NewRenderer();
        var stage = Placed(new Rect(0, 0, W, H));
        var viewport = Placed(new Rect(0, viewportTop, W, H - viewportTop));
        viewport.ClipToBounds = true;
        // It draws, as a scroll area's background does: the element's glow must not take the viewport's clip from it.
        viewport.RenderAction = s => s.DrawRectangle(Brushes.Blue, new Rect(0, 0, W, H - viewportTop));
        // Lime, so any red is the white glow.
        var element = RedLime(new Rect(8, 10 - viewportTop, 40, 40));
        element.RenderAction = s => s.DrawRectangle(Brushes.Lime, new Rect(0, 0, 40, 40));
        element.SoftEdge = soft;
        element.Aura = new Aura { Radius = 6, Color = Colors.White, Opacity = 1, Turbulence = turbulence, Flow = 0 };
        viewport.Add(element);
        stage.Add(viewport);
        var root = new VisualRoot(stage, W, H);
        for (var i = 0; i < frames; i++)
        {
            Draw(renderer, root);
        }

        var pixels = Pixels(renderer);
        var column = new int[H];
        for (var row = 0; row < H; row++)
        {
            column[row] = Red(pixels, 6, row);
        }

        return column;
    }

    [TestCase(0)]
    [TestCase(4)]
    public void AnOwnClipCarriedPastTheWindow_KeepsTheReflectionWhole(int radius)
    {
        using var renderer = NewRenderer();
        var stage = Placed(new Rect(0, 0, W, H));
        var moved = Placed(new Rect(0, 0, W, H));
        moved.IsRenderMotionNode = true;
        var element = RedLime(new Rect(4, 4, 40, 40));
        element.ClipToBounds = true;
        element.ClipCornerRadius = new ProceduralGeometry.CornerRadius(radius);
        moved.Add(element);
        stage.Add(moved);
        var root = new VisualRoot(stage, W, H);
        Draw(renderer, root);
        Draw(renderer, root);

        moved.Bounds = new Rect(0, -14, W, H);
        Draw(renderer, root);

        // Rows -10 to 30 now, the floor at 30: rows -10 to 0 mirrored at 60 to 70.
        Assert.That(Red(Pixels(renderer), 24, 65), Is.GreaterThan(200));
    }

    [TestCase(0)]
    [TestCase(4)]
    public void ARoundedOwnClipPastTheWindow_HoldsOnAReplayedFrame(int radius)
    {
        using var renderer = NewRenderer();
        var stage = Placed(new Rect(0, 0, W, H));
        var element = RedLime(new Rect(4, -10, 40, 40));
        element.ClipToBounds = true;
        element.ClipCornerRadius = new ProceduralGeometry.CornerRadius(radius);
        // A living aura draws the layer again every frame.
        element.Aura = new Aura { Radius = 4, Color = Colors.White, Opacity = 1, Turbulence = 1 };
        stage.Add(element);
        var root = new VisualRoot(stage, W, H);
        Draw(renderer, root);
        Draw(renderer, root);
        Draw(renderer, root);
        var pixels = Pixels(renderer);

        Assert.Multiple(() =>
        {
            Assert.That(renderer.Cache.LastFrameReplayed, Is.True, "a replayed frame");
            Assert.That(Red(pixels, 24, 65), Is.GreaterThan(200), "the red above the window, mirrored");
            Assert.That(Red(pixels, 24, 0), Is.GreaterThan(245), "and no edge where the window cuts the clip");
        });
    }

    [Test]
    public void InkInAReflectedElement_IsDrawn()
    {
        using var renderer = NewRenderer();
        var stage = Placed(new Rect(0, 0, W, H));
        var element = Placed(new Rect(4, 40, 40, 20));
        element.Reflection = new Reflection { Size = 1, Opacity = 1, Fade = 0 };
        var ink = new InkBrush
        {
            Points = [new Vector2F(2, 10), new Vector2F(38, 10)], Count = 2, Thickness = 6, Color = Colors.Red
        };
        element.RenderAction = s => s.DrawRectangle(ink, new Rect(0, 0, 40, 20));
        stage.Add(element);
        var root = new VisualRoot(stage, W, H);
        Draw(renderer, root);
        Draw(renderer, root);

        Assert.That(Red(Pixels(renderer), 24, 50), Is.GreaterThan(200), "the stroke at row 50");
    }

    // A reflection out of view asks nothing past the window: the layer stays the window's size, not the element's.
    [Test]
    public void ATallElementsLayer_IsNoTallerThanItsView()
    {
        using var renderer = NewRenderer();
        var stage = Placed(new Rect(0, 0, W, H));
        var element = Placed(new Rect(4, 4, 40, 3000));
        element.Reflection = new Reflection { Size = 1, Opacity = 1, Fade = 0 };
        element.RenderAction = s => s.DrawRectangle(Brushes.Red, new Rect(0, 0, 40, 3000));
        stage.Add(element);
        var root = new VisualRoot(stage, W, H);
        Draw(renderer, root);
        Draw(renderer, root);

        Assert.That(renderer.Cache.TallestLayerTexture, Is.LessThanOrEqualTo((uint)(2 * H)));
    }

    private sealed class StubResourceFactory : IResourceFactory
    {
        public uint MaxTextureDimension => uint.MaxValue;
        public uint MaxTextureArrayLayers => uint.MaxValue;

        private Adamantium.Graphics.Fonts.FontRenderer _fontRenderer;

        public ITexture CreateTexture(TextureDescription description, byte[] pixelData) => throw new NotSupportedException();
        public ITexture CreateTextureArray(TextureDescription description, System.Collections.Generic.IReadOnlyList<byte[]> layers) => throw new NotSupportedException();
        public ITexture ImportSharedSurface(SharedSurfaceDescriptor descriptor) => throw new NotSupportedException();
        public IRenderTarget CreateRenderTarget(uint width, uint height, MSAALevel msaa, Adamantium.Imaging.SurfaceFormat format, Adamantium.Vulkan.Core.ImageLayout desiredLayout) => throw new NotSupportedException();
        public Adamantium.Graphics.Fonts.FontRenderer GetFontRenderer(IGraphicsDevice graphicsDevice) => _fontRenderer ??= new Adamantium.Graphics.Fonts.FontRenderer(graphicsDevice);
    }
}
