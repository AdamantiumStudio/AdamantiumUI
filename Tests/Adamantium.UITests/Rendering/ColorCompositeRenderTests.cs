using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

// Google's test font draws one glyph per 'COLR' composite mode: a yellow square, and over it, composited by the mode, a
// blue one, the two in a group over a black square. Where only the first lies, where only the second, and where both
// overlap, the glyph shader must give what the W3C compositing formulas give, written here on their own; the group lies
// over black, so its color is what shows.
[TestFixture]
[Category("Gpu")]
public class ColorCompositeRenderTests
{
    private const int Width = 160;
    private const int Height = 140;
    private const double Size = 96;
    private const double Margin = 40;
    private const int Tolerance = 4;

    private static Typeface typeface;

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

    private sealed class GlyphView : MeasurableUIComponent
    {
        private readonly string _text;

        public GlyphView(string text)
        {
            _text = text;
            Layout = new TextLayout(typeface, typeface.GetFont(0)) { Fallback = null };
        }

        public TextLayout Layout { get; }

        protected override Size MeasureOverride(Size availableSize)
        {
            return Layout.ProcessText(new AttributedText(_text), Size, new Size(double.NaN, double.NaN),
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
            context.ForControl(this).DrawText(parameters, DesiredSize, Layout, Brushes.White, Brushes.Transparent,
                Brushes.Transparent);
        }
    }

    public static IEnumerable<TestCaseData> Modes() =>
        Enumerable.Range(0, 28).Select(m => new TestCaseData((ColorCompositeMode)m).SetName($"{(ColorCompositeMode)m}"));

    [TestCaseSource(nameof(Modes))]
    public void TheShaderComposites_AsTheFormulasSay(ColorCompositeMode mode)
    {
        typeface ??= Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts",
            "test_glyphs-glyf_colr_1.ttf"), 3);
        var font = typeface.GetFont(0);
        var codepoint = 0xF0A00 + (int)mode;
        font.TryGetGlyphIndex(codepoint, out var glyph);
        var operations = font.GetColorPaint(glyph);
        var fills = operations.Where(o => o.Kind == ColorPaintOperationKind.Fill).Select(o => o.Fill.Stops[0].Color.Value)
            .ToArray();
        var clips = operations.Where(o => o.Kind == ColorPaintOperationKind.PushClip)
            .Select(o => Box(font.GetGlyphByIndex(o.GlyphIndex).BoundingRectangle, o.Transform)).ToArray();
        var composites = operations.Where(o => o.Kind == ColorPaintOperationKind.PopGroup)
            .Select(o => o.Mode).Where(m => m != ColorCompositeMode.SourceOver).ToArray();
        Assert.That(composites.Length == 0 ? ColorCompositeMode.SourceOver : composites[0], Is.EqualTo(mode));
        Assert.That((fills.Length, clips.Length), Is.EqualTo((3, 3)),
            "a black square under a group of a backdrop and a source, each a solid square");

        var view = new GlyphView(char.ConvertFromUtf32(codepoint));
        var pixels = Render(view);
        var scale = Size / font.UnitsPerEm;
        var penX = view.Layout.GetTextData()[0].PenX;
        var baseline = Math.Round(view.Layout.GetLine(0).Baseline);
        var backdrop = Premultiplied(fills[1]);
        var source = Premultiplied(fills[2]);
        var checkedRegions = 0;
        foreach (var (inBackdrop, inSource) in new[] { (true, false), (false, true), (true, true) })
        {
            if (!TryFindPoint(clips[1], clips[2], inBackdrop, inSource, out var point))
            {
                continue;
            }

            var x = (int)Math.Floor(penX + point.X * scale);
            var y = (int)Math.Floor(baseline - point.Y * scale);
            var expected = Composite(mode, inSource ? source : Transparent, inBackdrop ? backdrop : Transparent);
            var at = (y * Width + x) * 4;
            var actual = new[] { pixels[at + 2], pixels[at + 1], pixels[at] };
            var wanted = expected.Take(3).Select(c => (int)Math.Round(c * 255)).ToArray();
            Assert.That(actual.Zip(wanted, (a, w) => Math.Abs(a - w)).Max(), Is.LessThanOrEqualTo(Tolerance),
                $"{(inBackdrop ? "backdrop" : "")}{(inSource ? " source" : "")} at ({x}, {y}): " +
                $"expected {string.Join(",", wanted)}, drawn {string.Join(",", actual)}");
            checkedRegions++;
        }

        Assert.That(checkedRegions, Is.EqualTo(3), "each region has a point to look at");
    }

    private static readonly double[] Transparent = [0, 0, 0, 0];

    private static double[] Premultiplied(Color color)
    {
        var a = color.A / 255.0;
        return [color.R / 255.0 * a, color.G / 255.0 * a, color.B / 255.0 * a, a];
    }

    private static RectangleF Box(Rectangle bounds, Matrix3x2 transform)
    {
        var a = Matrix3x2.TransformPoint(transform, new Vector2(bounds.X, bounds.Y));
        var b = Matrix3x2.TransformPoint(transform, new Vector2(bounds.X + bounds.Width, bounds.Y + bounds.Height));
        var minX = Math.Min(a.X, b.X);
        var minY = Math.Min(a.Y, b.Y);
        return new RectangleF((float)minX, (float)minY, (float)(Math.Max(a.X, b.X) - minX), (float)(Math.Max(a.Y, b.Y) - minY));
    }

    private static bool TryFindPoint(RectangleF backdrop, RectangleF source, bool inBackdrop, bool inSource,
        out Vector2 point)
    {
        for (var y = Math.Min(backdrop.Y, source.Y); y <= Math.Max(backdrop.Bottom, source.Bottom); y += 10)
        {
            for (var x = Math.Min(backdrop.X, source.X); x <= Math.Max(backdrop.Right, source.Right); x += 10)
            {
                if (Inside(backdrop, x, y) == inBackdrop && Inside(source, x, y) == inSource &&
                    Far(backdrop, x, y) && Far(source, x, y))
                {
                    point = new Vector2(x, y);
                    return true;
                }
            }
        }

        point = default;
        return false;
    }

    private static bool Inside(RectangleF box, double x, double y) =>
        x > box.X && x < box.Right && y > box.Y && y < box.Bottom;

    private static bool Far(RectangleF box, double x, double y) =>
        Math.Abs(x - box.X) > Margin && Math.Abs(x - box.Right) > Margin &&
        Math.Abs(y - box.Y) > Margin && Math.Abs(y - box.Bottom) > Margin;

    private static double[] Composite(ColorCompositeMode mode, double[] s, double[] d)
    {
        var sa = s[3];
        var da = d[3];
        switch (mode)
        {
            case ColorCompositeMode.Clear:
                return [0, 0, 0, 0];
            case ColorCompositeMode.Source:
                return s;
            case ColorCompositeMode.Destination:
                return d;
            case ColorCompositeMode.SourceOver:
                return Mix(s, 1, d, 1 - sa);
            case ColorCompositeMode.DestinationOver:
                return Mix(s, 1 - da, d, 1);
            case ColorCompositeMode.SourceIn:
                return Mix(s, da, d, 0);
            case ColorCompositeMode.DestinationIn:
                return Mix(s, 0, d, sa);
            case ColorCompositeMode.SourceOut:
                return Mix(s, 1 - da, d, 0);
            case ColorCompositeMode.DestinationOut:
                return Mix(s, 0, d, 1 - sa);
            case ColorCompositeMode.SourceAtop:
                return Mix(s, da, d, 1 - sa);
            case ColorCompositeMode.DestinationAtop:
                return Mix(s, 1 - da, d, sa);
            case ColorCompositeMode.Xor:
                return Mix(s, 1 - da, d, 1 - sa);
            case ColorCompositeMode.Plus:
                return s.Zip(d, (a, b) => Math.Min(1, a + b)).ToArray();
        }

        var cs = Unpremultiplied(s);
        var cb = Unpremultiplied(d);
        var blended = Blend(mode, cb, cs);
        var result = new double[4];
        for (var i = 0; i < 3; i++)
        {
            result[i] = s[i] * (1 - da) + d[i] * (1 - sa) + sa * da * blended[i];
        }

        result[3] = sa + da - sa * da;
        return result;
    }

    private static double[] Mix(double[] s, double fs, double[] d, double fd) =>
        s.Zip(d, (a, b) => a * fs + b * fd).ToArray();

    private static double[] Unpremultiplied(double[] c) =>
        c[3] > 0 ? [c[0] / c[3], c[1] / c[3], c[2] / c[3]] : [0, 0, 0];

    private static double[] Blend(ColorCompositeMode mode, double[] cb, double[] cs)
    {
        switch (mode)
        {
            case ColorCompositeMode.Hue:
                return SetLum(SetSat(cs, Sat(cb)), Lum(cb));
            case ColorCompositeMode.Saturation:
                return SetLum(SetSat(cb, Sat(cs)), Lum(cb));
            case ColorCompositeMode.Color:
                return SetLum(cs, Lum(cb));
            case ColorCompositeMode.Luminosity:
                return SetLum(cb, Lum(cs));
        }

        var result = new double[3];
        for (var i = 0; i < 3; i++)
        {
            result[i] = Separable(mode, cb[i], cs[i]);
        }

        return result;
    }

    private static double Separable(ColorCompositeMode mode, double b, double s)
    {
        switch (mode)
        {
            case ColorCompositeMode.Multiply:
                return b * s;
            case ColorCompositeMode.Screen:
                return b + s - b * s;
            case ColorCompositeMode.Overlay:
                return HardLight(s, b);
            case ColorCompositeMode.Darken:
                return Math.Min(b, s);
            case ColorCompositeMode.Lighten:
                return Math.Max(b, s);
            case ColorCompositeMode.ColorDodge:
                return b == 0 ? 0 : s >= 1 ? 1 : Math.Min(1, b / (1 - s));
            case ColorCompositeMode.ColorBurn:
                return b >= 1 ? 1 : s == 0 ? 0 : 1 - Math.Min(1, (1 - b) / s);
            case ColorCompositeMode.HardLight:
                return HardLight(b, s);
            case ColorCompositeMode.SoftLight:
                if (s <= 0.5)
                {
                    return b - (1 - 2 * s) * b * (1 - b);
                }

                var lift = b <= 0.25 ? ((16 * b - 12) * b + 4) * b : Math.Sqrt(b);
                return b + (2 * s - 1) * (lift - b);
            case ColorCompositeMode.Difference:
                return Math.Abs(b - s);
            case ColorCompositeMode.Exclusion:
                return b + s - 2 * b * s;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }
    }

    private static double HardLight(double b, double s) => s <= 0.5 ? b * 2 * s : b + (2 * s - 1) - b * (2 * s - 1);

    private static double Lum(double[] c) => 0.3 * c[0] + 0.59 * c[1] + 0.11 * c[2];

    private static double[] SetLum(double[] c, double l)
    {
        var d = l - Lum(c);
        double[] result = [c[0] + d, c[1] + d, c[2] + d];
        var lum = Lum(result);
        var min = result.Min();
        var max = result.Max();
        for (var i = 0; i < 3; i++)
        {
            if (min < 0)
            {
                result[i] = lum + (result[i] - lum) * lum / (lum - min);
            }

            if (max > 1)
            {
                result[i] = lum + (result[i] - lum) * (1 - lum) / (max - lum);
            }
        }

        return result;
    }

    private static double Sat(double[] c) => c.Max() - c.Min();

    private static double[] SetSat(double[] c, double s)
    {
        var order = new[] { 0, 1, 2 }.OrderBy(i => c[i]).ToArray();
        var result = new double[3];
        var spread = c[order[2]] - c[order[0]];
        if (spread > 0)
        {
            result[order[1]] = (c[order[1]] - c[order[0]]) * s / spread;
            result[order[2]] = s;
        }

        return result;
    }

    private static byte[] Render(GlyphView view)
    {
        var device = GpuTestDevice.Device;
        var factory = new RenderUnitFactory(device, new FontResourceFactory());
        using var renderer = new OffscreenTestRenderer(device, factory, Width, Height) { ClearColor = Colors.Black };
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children.Add(view);
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
