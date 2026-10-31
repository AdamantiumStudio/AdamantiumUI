using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Fonts;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Rendering;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

// A long text block builds quads only for a band around what the window shows, and scrolled past it, draws what a full
// walk draws.
[TestFixture]
[Category("Gpu")]
public class LongTextBandRenderTests
{
    private const int Dim = 128;
    private const int Lines = 400;

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
    private VisualRoot _root;
    private Canvas _canvas;
    private TextBlock _block;

    [SetUp]
    public void SetUp()
    {
        var device = GpuTestDevice.Device;
        _renderer = new OffscreenTestRenderer(device, new RenderUnitFactory(device, new FontResourceFactory()), Dim, Dim)
            { ClearColor = Colors.Black };
        _block = new TextBlock
        {
            Text = string.Join("\n", Enumerable.Range(0, Lines).Select(i => $"line {i:000}")),
            Foreground = Brushes.White,
            FontSize = 18
        };
        _canvas = new Canvas();
        _canvas.Children.Add(_block);
        _root = new VisualRoot(_canvas, Dim, Dim);
        Draw();
    }

    [TearDown]
    public void TearDown() => _renderer.Dispose();

    private void Draw()
    {
        ((IMeasurableComponent)_root).Measure(new Size<double>(Dim, Dim));
        ((IMeasurableComponent)_root).Arrange(new Rect(0, 0, Dim, Dim));
        Assert.That(_renderer.RenderFrame(_root), Is.True, "off-screen frame must render");
        RenderDirty.Clear();
    }

    private byte[] Pixels()
    {
        using var img = _renderer.RenderTarget.ResolveTexture.ToImage();
        var bytes = new byte[(int)img.TotalSizeInBytes];
        Marshal.Copy(img.DataPointer, bytes, 0, bytes.Length);
        return bytes;
    }

    private static int LitPixels(byte[] pixels)
    {
        var count = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0 || pixels[i + 1] != 0 || pixels[i + 2] != 0) count++;
        }

        return count;
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

    private double Offset(int line) => line * _block.Layout.GetLine(0).Height;

    private void ScrollTo(int line)
    {
        Canvas.SetTop(_block, -Offset(line));
        Draw();
    }

    private static int LitPixelsRightOf(byte[] pixels, int x)
    {
        var count = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (i / 4 % Dim >= x && (pixels[i] != 0 || pixels[i + 1] != 0 || pixels[i + 2] != 0)) count++;
        }

        return count;
    }

    [Test]
    public void ALongText_BuildsQuadsOnlyAroundTheWindow()
    {
        var layout = _block.Layout;

        Assert.Multiple(() =>
        {
            Assert.That(layout.GlyphCount, Is.GreaterThan(3000), "long enough to be banded");
            Assert.That(layout.ElementsCount, Is.GreaterThan(0u), "what the window shows is built");
            Assert.That(layout.ElementsCount, Is.LessThan((uint)layout.GlyphCount / 5), "and not the rest");
            Assert.That(LitPixels(Pixels()), Is.GreaterThan(0), "the first lines are drawn");
        });
    }

    [Test]
    public void ScrolledPastTheBand_DrawsWhatAFullWalkDraws()
    {
        ScrollTo(200);
        var scrolled = Pixels();
        var band = _block.Layout.QuadWindow;

        RenderDirty.MarkStructural();
        Draw();

        Assert.Multiple(() =>
        {
            Assert.That(band, Is.Not.Null);
            Assert.That(band.Value.Top, Is.EqualTo(Offset(200) - Dim).Within(1), "a window above what is shown");
            Assert.That(band.Value.Height, Is.EqualTo(3 * Dim).Within(1), "the window shown, and one below it");
            Assert.That(LitPixels(scrolled), Is.GreaterThan(0), "the lines scrolled to are drawn");
            Assert.That(_renderer.Cache.LastFrameReplayed, Is.False, "the reference frame has to actually walk");
            Assert.That(DifferingPixels(scrolled, Pixels()), Is.Zero, "the scrolled frame must equal a full walk");
        });
    }

    [Test]
    public void ScrolledWithinTheBand_KeepsItsQuads()
    {
        var band = _block.Layout.QuadWindow;

        ScrollTo(1);

        Assert.Multiple(() =>
        {
            Assert.That(band?.Height, Is.EqualTo(3 * Dim).Within(1), "fitted to the window");
            Assert.That(_block.Layout.QuadWindow, Is.EqualTo(band), "a line down is still inside the band");
        });
    }

    // A band picks glyphs by where they were laid; an effect may bring one into view from far outside it.
    [Test]
    public void ALongTextWithAnEffect_BuildsTheWholeText_SoAGlyphMovedIntoViewIsDrawn()
    {
        var before = LitPixelsRightOf(Pixels(), Dim / 2);
        var farLine = _block.Text.IndexOf("line 300", StringComparison.Ordinal);
        _block.TextEffects.Add(new TextEffect
        {
            PositionStart = farLine,
            PositionCount = 8,
            TransformOrigin = TextEffectOrigin.Element,
            Transform = new Transform { TranslateX = Dim / 2.0, TranslateY = -Offset(300) },
        });
        Draw();

        Assert.Multiple(() =>
        {
            Assert.That(_block.Layout.QuadWindow, Is.Null, "no band for a text whose glyphs may move");
            Assert.That(LitPixelsRightOf(Pixels(), Dim / 2), Is.GreaterThan(before), "the line moved into view is drawn");
        });
    }

    [Test]
    public void ACopyElsewhere_BuildsTheWholeText()
    {
        _canvas.RenderClones = [Matrix4x4F.Identity, Matrix4x4.CreateTranslation<float>(Dim / 2f, (float)-Offset(200), 0)];
        RenderDirty.MarkStructural();
        Draw();

        Assert.Multiple(() =>
        {
            Assert.That(_block.Layout.QuadWindow, Is.Null, "copies show more than one window");
            Assert.That(LitPixelsRightOf(Pixels(), Dim / 2), Is.GreaterThan(0), "the copy shows the lines it was moved to");
        });
    }
}
