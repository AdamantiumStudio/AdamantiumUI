using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
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

// Glyphs of attributed text take their own colors on both text paths; glyphs without one take the element's.
[TestFixture]
[Category("Gpu")]
public class AttributedTextColorRenderTests
{
    private const int Width = 220;
    private const int Height = 48;

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

    private sealed class AttributedTextView : MeasurableUIComponent
    {
        private readonly AttributedText _text;
        private readonly TextLayout _layout;

        public AttributedTextView(AttributedText text)
        {
            _text = text;
            var font = DefaultFontFamily;
            _layout = new TextLayout(font.Typeface, font.Fonts[0]);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            return _layout.ProcessText(_text, 30, new Size(double.NaN, double.NaN), TextWrapping.NoWrap,
                TextTrimming.None, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
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

    [TestCase(true)]
    [TestCase(false)]
    public void EachRangeDrawsInItsOwnColor(bool batched)
    {
        var device = GpuTestDevice.Device;
        var factory = new RenderUnitFactory(device, new FontResourceFactory());
        using var renderer = new OffscreenTestRenderer(device, factory, Width, Height) { ClearColor = Colors.Black };

        var text = new AttributedText("HHHH HHHH HHHH")
            .Apply(0, 4, new TextAttributes { Foreground = Colors.Red })
            .Apply(5, 4, new TextAttributes { Foreground = Colors.Lime });
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children.Add(new AttributedTextView(text));
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
            var (red, green, white) = CountColors(pixels);

            Assert.That(red, Is.GreaterThan(20), "the first word is red");
            Assert.That(green, Is.GreaterThan(20), "the second word is green");
            Assert.That(white, Is.GreaterThan(20), "the third word takes the element's white");
        }
        finally
        {
            FontRenderer.UseTextBatch = wasBatched;
            FontAtlasStore.SynchronousFill = wasSynchronous;
        }
    }

    private static (int Red, int Green, int White) CountColors(byte[] pixels)
    {
        int red = 0, green = 0, white = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            int b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];
            if (r > 150 && g < 60 && b < 60)
            {
                red++;
            }
            else if (g > 150 && r < 60 && b < 60)
            {
                green++;
            }
            else if (r > 150 && g > 150 && b > 150)
            {
                white++;
            }
        }

        return (red, green, white);
    }
}
