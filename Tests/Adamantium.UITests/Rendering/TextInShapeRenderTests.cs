using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Fonts;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Rendering;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

// A text block set inside a shape draws its letters inside it; one flowing round a shape keeps out of it.
[TestFixture]
[Category("Gpu")]
public class TextInShapeRenderTests
{
    private const int Size = 240;
    private const string Words = "the quick brown fox jumps over the lazy dog and runs on past the old mill by the river ";

    private sealed class FontResourceFactory : IResourceFactory
    {
        public uint MaxTextureDimension => uint.MaxValue;
        public uint MaxTextureArrayLayers => uint.MaxValue;

        private readonly Dictionary<IGraphicsDevice, FontRenderer> _renderers = new();

        public ITexture CreateTexture(TextureDescription description, byte[] pixelData) => throw new NotSupportedException();
        public ITexture CreateTextureArray(TextureDescription description, IReadOnlyList<byte[]> layers) => throw new NotSupportedException();
        public ITexture ImportSharedSurface(SharedSurfaceDescriptor descriptor) => throw new NotSupportedException();
        public IRenderTarget CreateRenderTarget(uint width, uint height, MSAALevel msaa, SurfaceFormat format, ImageLayout desiredLayout) => throw new NotSupportedException();

        public FontRenderer GetFontRenderer(IGraphicsDevice graphicsDevice)
        {
            if (!_renderers.TryGetValue(graphicsDevice, out var renderer))
            {
                _renderers[graphicsDevice] = renderer = new FontRenderer(graphicsDevice);
            }

            return renderer;
        }
    }

    private OffscreenTestRenderer _renderer;

    [SetUp]
    public void SetUp()
    {
        var device = GpuTestDevice.Device;
        _renderer = new OffscreenTestRenderer(device, new RenderUnitFactory(device, new FontResourceFactory()), Size, Size)
            { ClearColor = Colors.Black };
    }

    [TearDown]
    public void TearDown() => _renderer.Dispose();

    private static Geometry Circle(double x, double y, double radius) => (Geometry)TypeCastFactory.CastFromString(
        FormattableString.Invariant($"M {x},{y - radius} A {radius},{radius} 0 1 1 {x - 0.01},{y - radius} Z"),
        typeof(Geometry));

    // The lit pixels, and how many of them stand within `radius` of the middle.
    private (int Lit, int Inside) Draw(TextBlock block)
    {
        var canvas = new Canvas();
        canvas.Children.Add(block);
        var root = new VisualRoot(canvas, Size, Size);
        for (var i = 0; i < 4; i++)
        {
            ((IMeasurableComponent)root).Measure(new Size<double>(Size, Size));
            ((IMeasurableComponent)root).Arrange(new Rect(0, 0, Size, Size));
            Assert.That(_renderer.RenderFrame(root), Is.True, "off-screen frame must render");
            RenderDirty.Clear();
        }

        using var img = _renderer.RenderTarget.ResolveTexture.ToImage();
        var pixels = new byte[(int)img.TotalSizeInBytes];
        Marshal.Copy(img.DataPointer, pixels, 0, pixels.Length);
        int lit = 0, inside = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i] < 64 && pixels[i + 1] < 64 && pixels[i + 2] < 64)
            {
                continue;
            }

            var (x, y) = (i / 4 % Size + 0.5, i / 4 / Size + 0.5);
            lit++;
            inside += Math.Sqrt((x - 120) * (x - 120) + (y - 120) * (y - 120)) <= 80 ? 1 : 0;
        }

        return (lit, inside);
    }

    private static TextBlock Block() => new()
    {
        Text = Words + Words + Words,
        FontSize = 11,
        Foreground = Brushes.White,
        TextWrapping = TextWrapping.WrapByWords,
        VerticalTextAlignment = VerticalTextAlignment.Top,
        Width = Size,
        Height = Size,
    };

    [Test]
    public void TextInACircle_IsDrawnInsideIt()
    {
        var block = Block();
        block.Shape = Circle(120, 120, 80);

        var (lit, inside) = Draw(block);

        Assert.Multiple(() =>
        {
            Assert.That(lit, Is.GreaterThan(500));
            Assert.That(inside, Is.EqualTo(lit), "nothing outside the circle");
        });
    }

    [Test]
    public void TextTooLongForItsShape_IsOverset()
    {
        var block = Block();
        block.Shape = Circle(120, 120, 30);

        Draw(block);

        Assert.That(block.IsOverset, Is.True);
    }

    [Test]
    public void TextRoundACircle_KeepsOutOfIt()
    {
        var block = Block();
        block.ExclusionShape = Circle(120, 120, 80);
        block.ShapePadding = 2;

        var (lit, inside) = Draw(block);

        Assert.Multiple(() =>
        {
            Assert.That(lit, Is.GreaterThan(500));
            Assert.That(inside, Is.Zero, "nothing inside the circle");
        });
    }
}
