using System;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Fonts;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Animation;
using Adamantium.UI.Rendering;
using Adamantium.UI.Rendering.RenderUnits;
using Adamantium.Vulkan.Core;
using NUnit.Framework;
using Image = Adamantium.Imaging.Image;

namespace Adamantium.UITests.Rendering;

// Soft edges and a reflection are an element's own, as its shadow is: drawn through a layer, the edge melting in from the
// element's outline, the mirror image laid under its bottom edge and fading away downward.
[TestFixture]
[Category("Gpu")]
public class SoftEdgeReflectionRenderTests
{
    private IGraphicsDevice _device;

    [SetUp]
    public void SetUp() => _device = GpuTestDevice.Device;

    [TearDown]
    public void TearDown() => AnimationManager.Reset();

    // A 40x40 element at (4,4) painting its top half red and its bottom half lime, over blue.
    private static (TestRoot Root, TestControl Element) Element(Action<TestControl> set)
    {
        var root = new TestRoot(64, 128);
        var element = new TestControl
        {
            RenderAction = s => s
                .DrawRectangle(Brushes.Red, new Rect(0, 0, 40, 20))
                .DrawRectangle(Brushes.Lime, new Rect(0, 20, 40, 20))
        };
        element.Bounds = new Rect(4, 4, 40, 40);
        element.RenderSize = new Size(40, 40);
        set(element);
        root.Add(element);
        return (root, element);
    }

    private uint[] Render(TestRoot root, params (int X, int Y)[] at)
    {
        var factory = new RenderUnitFactory(_device, new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(_device, factory, 64, 128) { ClearColor = Colors.Blue };
        Assert.That(renderer.RenderFrame(root), Is.True);
        Assert.That(renderer.RenderFrame(root), Is.True);
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"soft_{Guid.NewGuid():N}.png");
        renderer.Save(path, ImageFileType.Png);
        try
        {
            var px = Image.Load(path).GetPixelBuffer(0, 0);
            var pixels = new uint[at.Length];
            for (var i = 0; i < at.Length; i++)
            {
                pixels[i] = px.GetPixel<uint>(at[i].X, at[i].Y);
            }

            return pixels;
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    private static int Red(uint pixel) => (int)(pixel & 0xFF);

    private static int Green(uint pixel) => (int)((pixel >> 8) & 0xFF);

    private static int Blue(uint pixel) => (int)((pixel >> 16) & 0xFF);

    [Test]
    public void ASoftEdge_MeltsTheElementFromItsOutlineIn()
    {
        var (root, _) = Element(e => e.SoftEdge = 10);

        var px = Render(root, (5, 19), (9, 19), (24, 19));

        Assert.Multiple(() =>
        {
            Assert.That(Red(px[0]), Is.LessThan(60), "barely there a pixel in from the edge");
            Assert.That(Red(px[1]), Is.InRange(80, 170), "half way through the fade, about half");
            Assert.That(Red(px[2]), Is.GreaterThan(240), "past the fade, all of it");
        });
    }

    [Test]
    public void ASoftEdge_FollowsTheRoundedCorners()
    {
        var (square, _) = Element(e => e.SoftEdge = 4);
        var (rounded, _) = Element(e =>
        {
            e.SoftEdge = 4;
            e.ClipCornerRadius = new ProceduralGeometry.CornerRadius(12);
        });

        var corner = (7, 7);
        var squareCorner = Render(square, corner)[0];
        var roundedCorner = Render(rounded, corner)[0];

        Assert.Multiple(() =>
        {
            Assert.That(Red(squareCorner), Is.GreaterThan(120), "three pixels in from a square corner, most of it");
            Assert.That(Red(roundedCorner), Is.LessThan(10), "outside the rounded one, nothing");
        });
    }

    [Test]
    public void AReflection_MirrorsTheElementUnderItsBottomEdge_AndFadesAway()
    {
        var (root, _) = Element(e => e.Reflection = new Reflection { Size = 1, Opacity = 1 });

        // The bottom edge is row 44: the lime half is mirrored first, then the red one.
        var px = Render(root, (24, 46), (24, 70), (24, 86), (24, 100));

        Assert.Multiple(() =>
        {
            Assert.That(Green(px[0]), Is.GreaterThan(200), "right under the edge, the lime, nearly as strong");
            Assert.That(Red(px[1]), Is.InRange(60, 200), "further down, the red, fading");
            Assert.That(Red(px[1]), Is.LessThan(Red(Render(Element(e => { }).Root, (24, 10))[0])), "weaker than itself");
            Assert.That(Red(px[2]) + Green(px[2]), Is.LessThan(20), "past its length, gone");
            Assert.That(Blue(px[3]), Is.GreaterThan(240), "and the background below untouched");
        });
    }

    [Test]
    public void AReflectionsDistance_LeavesAGapUnderTheEdge()
    {
        var (root, _) = Element(e => e.Reflection = new Reflection { Size = 1, Opacity = 1, Distance = 8 });

        var px = Render(root, (24, 47), (24, 54));

        Assert.Multiple(() =>
        {
            Assert.That(Blue(px[0]), Is.GreaterThan(240), "in the gap, the background");
            Assert.That(Green(px[1]), Is.GreaterThan(150), "the reflection starts below it");
        });
    }

    [Test]
    public void AReflectionSwitchedOff_DrawsNothingUnderTheElement()
    {
        var (root, _) = Element(e => e.Reflection = new Reflection { Size = 1, Opacity = 1, IsEnabled = false });

        var px = Render(root, (24, 46), (24, 24));

        Assert.Multiple(() =>
        {
            Assert.That(Blue(px[0]), Is.GreaterThan(240), "no reflection");
            Assert.That(Green(px[1]), Is.GreaterThan(240), "the element itself as it was");
        });
    }

    [Test]
    public void AReflectionChangedInPlace_ShowsOnTheNextFrame()
    {
        var reflection = new Reflection { Size = 1, Opacity = 1 };
        var (root, _) = Element(e => e.Reflection = reflection);
        var factory = new RenderUnitFactory(_device, new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(_device, factory, 64, 128) { ClearColor = Colors.Blue };
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();

        reflection.Distance = 20;
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();

        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"soft_{Guid.NewGuid():N}.png");
        renderer.Save(path, ImageFileType.Png);
        try
        {
            var px = Image.Load(path).GetPixelBuffer(0, 0).GetPixel<uint>(24, 50);
            Assert.That(Blue(px), Is.GreaterThan(240), "the gap opened");
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Test]
    public void AnElementThatClipsToItsBounds_StillShowsItsReflection()
    {
        var (root, _) = Element(e =>
        {
            e.ClipToBounds = true;
            e.Reflection = new Reflection { Size = 1, Opacity = 1 };
        });

        var px = Render(root, (24, 46));

        Assert.That(Green(px[0]), Is.GreaterThan(200), "its own clip does not cut what hangs below it");
    }

    [Test]
    public void AReflectionInsideAnotherLayer_IsNotCutAtItsEdge()
    {
        var root = new TestRoot(64, 128);
        var outer = new TestControl { IsLayer = true };
        outer.Bounds = new Rect(0, 0, 64, 50);
        outer.RenderSize = new Size(64, 50);
        var element = new TestControl
        {
            Reflection = new Reflection { Size = 1, Opacity = 1 },
            RenderAction = s => s.DrawRectangle(Brushes.Lime, new Rect(0, 0, 40, 40))
        };
        element.Bounds = new Rect(4, 4, 40, 40);
        element.RenderSize = new Size(40, 40);
        outer.Add(element);
        root.Add(outer);

        var px = Render(root, (24, 56));

        Assert.That(Green(px[0]), Is.GreaterThan(120), "twelve pixels down, still there");
    }

    [Test]
    public void AReflectionSwitchedOff_CostsNoLayer()
    {
        var (root, _) = Element(e => e.Reflection = new Reflection { IsEnabled = false });
        var factory = new RenderUnitFactory(_device, new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(_device, factory, 64, 128) { ClearColor = Colors.Blue };
        Assert.That(renderer.RenderFrame(root), Is.True);

        Assert.That(renderer.Cache.LayersRedrawn + renderer.Cache.LayersReused, Is.Zero);
    }

    [Test]
    public void ASoftEdgeOnAnElementOfNoSize_LeavesWhatItsChildrenDraw()
    {
        var root = new TestRoot(64, 128);
        var holder = new TestControl { SoftEdge = 6 };
        holder.Bounds = new Rect(4, 4, 0, 0);
        holder.RenderSize = new Size(0, 0);
        var bar = new TestControl { RenderAction = s => s.DrawRectangle(Brushes.Red, new Rect(0, 0, 40, 20)) };
        bar.Bounds = new Rect(0, 0, 40, 20);
        bar.RenderSize = new Size(40, 20);
        holder.Add(bar);
        root.Add(holder);

        var px = Render(root, (24, 14));

        Assert.That(Red(px[0]), Is.GreaterThan(240));
    }

    [Test]
    public void WithNeither_TheElementIsNoLayer()
    {
        var (root, _) = Element(e => { });
        var factory = new RenderUnitFactory(_device, new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(_device, factory, 64, 128) { ClearColor = Colors.Blue };
        Assert.That(renderer.RenderFrame(root), Is.True);

        Assert.That(renderer.Cache.LayersRedrawn + renderer.Cache.LayersReused, Is.Zero);
    }

    private sealed class StubResourceFactory : IResourceFactory
    {
        public uint MaxTextureDimension => uint.MaxValue;
        public uint MaxTextureArrayLayers => uint.MaxValue;

        private FontRenderer _fontRenderer;

        public ITexture CreateTexture(TextureDescription description, byte[] pixelData) => throw new NotSupportedException();
        public ITexture CreateTextureArray(TextureDescription description, System.Collections.Generic.IReadOnlyList<byte[]> layers) => throw new NotSupportedException();
        public ITexture ImportSharedSurface(SharedSurfaceDescriptor descriptor) => throw new NotSupportedException();
        public IRenderTarget CreateRenderTarget(uint width, uint height, MSAALevel msaa, SurfaceFormat format, ImageLayout desiredLayout) => throw new NotSupportedException();
        public FontRenderer GetFontRenderer(IGraphicsDevice graphicsDevice) => _fontRenderer ??= new FontRenderer(graphicsDevice);
    }
}
