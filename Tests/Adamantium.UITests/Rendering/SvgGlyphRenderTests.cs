using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Adamantium.Fonts;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Fonts;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Rendering;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

// Google builds its samples font from the same drawings as SVG documents ('SVG ') and as a 'COLR' version 1 paint
// graph. The paint graph is checked against HarfBuzz; the SVG glyphs drawn from their documents must look the same.
[TestFixture]
[Category("Gpu")]
public class SvgGlyphRenderTests
{
    private const int Width = 160;
    private const int Height = 80;

    private sealed class FontResourceFactory : IResourceFactory
    {
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

    private sealed class FontTextView : MeasurableUIComponent
    {
        private readonly string _text;
        private readonly TextLayout _layout;

        public FontTextView(string file, string text)
        {
            _text = text;
            var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", file), 3);
            _layout = new TextLayout(typeface, typeface.GetFont(0)) { Fallback = null };
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            return _layout.ProcessText(new AttributedText(_text), 56, new Size(double.NaN, double.NaN),
                TextWrapping.NoWrap, TextTrimming.None, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        }

        protected override void OnRender(IDrawingContext context)
        {
            var parameters = new TextRenderingParameters
            {
                HorizontalTextAlignment = HorizontalTextAlignment.Left,
                VerticalTextAlignment = VerticalTextAlignment.Top,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.None,
                Color = Colors.White,
                TextArea = new Rectangle(new Vector2F(), DesiredSize)
            };
            context.ForControl(this).DrawText(parameters, DesiredSize, _layout, Brushes.White, Brushes.Transparent,
                Brushes.Transparent);
        }
    }

    [TestCase("simple_linear")]
    [TestCase("simple_radial")]
    [TestCase("linear_repeat")]
    [TestCase("radial_reflect")]
    [TestCase("linear_transform")]
    [TestCase("radial_transform")]
    [TestCase("gradient_stop_opacity")]
    public void AnSvgGlyph_LooksLikeTheSameDrawingAsAPaintGraph(string word)
    {
        var svg = Render("samples-picosvg.ttf", word);
        var paint = Render("samples-glyf_colr_1.ttf", word);

        var inked = 0;
        var differing = 0;
        for (var i = 0; i < svg.Length; i += 4)
        {
            var difference = 0;
            for (var c = 0; c < 3; c++)
            {
                difference = Math.Max(difference, Math.Abs(svg[i + c] - paint[i + c]));
            }

            if (paint[i] + paint[i + 1] + paint[i + 2] > 30)
            {
                inked++;
            }

            if (difference > 48)
            {
                differing++;
            }
        }

        Assert.That(inked, Is.GreaterThan(400), "the paint graph draws the sample");
        Assert.That(differing, Is.LessThan(inked / 40), "the SVG glyph differs at its edges at most");
    }

    private static byte[] Render(string file, string text)
    {
        var device = GpuTestDevice.Device;
        var factory = new RenderUnitFactory(device, new FontResourceFactory());
        using var renderer = new OffscreenTestRenderer(device, factory, Width, Height) { ClearColor = Colors.Black };
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children.Add(new FontTextView(file, text));
        var root = new VisualRoot(stack, Width, Height);
        var wasSynchronous = FontAtlasStore.SynchronousFill;
        FontAtlasStore.SynchronousFill = true;
        try
        {
            ((IMeasurableComponent)root).Measure(new Size(Width, Height));
            ((IMeasurableComponent)root).Arrange(new Rect(0, 0, Width, Height));
            Assert.That(renderer.RenderFrame(root), Is.True, "off-screen frame must render");
            RenderDirty.Clear();
            using var image = renderer.RenderTarget.ResolveTexture.ReadbackToImage();
            var pixels = new byte[(int)image.TotalSizeInBytes];
            Marshal.Copy(image.DataPointer, pixels, 0, pixels.Length);
            return pixels;
        }
        finally
        {
            FontAtlasStore.SynchronousFill = wasSynchronous;
        }
    }
}
