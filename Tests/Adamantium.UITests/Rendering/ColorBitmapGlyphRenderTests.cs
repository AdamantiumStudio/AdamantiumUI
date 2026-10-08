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

// A glyph drawn from a color image ('CBDT'): Noto Color Emoji's red "⁉" comes out red on both text paths, at a size
// near the image's and at one that reads a deep level of its mip chain.
[TestFixture]
[Category("Gpu")]
public class ColorBitmapGlyphRenderTests
{
    private const int Width = 120;
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

    private sealed class BitmapTextView : MeasurableUIComponent
    {
        private readonly string _text;
        private readonly double _size;
        private readonly TextLayout _layout;

        public BitmapTextView(string text, double size)
        {
            _text = text;
            _size = size;
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "NotoColorEmoji.subset.ttf");
            var typeface = Typeface.LoadFont(path, 3);
            _layout = new TextLayout(typeface, typeface.GetFont(0)) { Fallback = null };
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            return _layout.ProcessText(new AttributedText(_text), _size, new Size(double.NaN, double.NaN),
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

    [TestCase(true, 48, 150)]
    [TestCase(false, 48, 150)]
    [TestCase(true, 9, 4)]
    [TestCase(false, 9, 4)]
    public void TheImageDrawsInItsOwnColors(bool batched, double size, int minimumRed)
    {
        var device = GpuTestDevice.Device;
        var factory = new RenderUnitFactory(device, new FontResourceFactory());
        using var renderer = new OffscreenTestRenderer(device, factory, Width, Height) { ClearColor = Colors.Black };

        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children.Add(new BitmapTextView("\U00002049", size));
        var root = new VisualRoot(stack, Width, Height);

        var wasBatched = FontRenderer.UseTextBatch;
        var wasSynchronous = FontAtlasStore.SynchronousFill;
        FontRenderer.UseTextBatch = batched;
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
            var (red, white) = CountColors(pixels);

            Assert.That(red, Is.GreaterThanOrEqualTo(minimumRed), "the image's red marks");
            Assert.That(white, Is.Zero, "nothing takes the element's white foreground");
        }
        finally
        {
            FontRenderer.UseTextBatch = wasBatched;
            FontAtlasStore.SynchronousFill = wasSynchronous;
        }
    }

    private static (int Red, int White) CountColors(byte[] pixels)
    {
        int red = 0, white = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            int b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];
            if (r > 120 && g < 70 && b < 70)
            {
                red++;
            }
            else if (r > 200 && g > 200 && b > 200)
            {
                white++;
            }
        }

        return (red, white);
    }
}
