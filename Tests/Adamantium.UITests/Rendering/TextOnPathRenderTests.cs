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

// Text on a path: each letter stood on the path and turned with it, nothing where the text does not fit.
[TestFixture]
[Category("Gpu")]
public class TextOnPathRenderTests
{
    private const int Width = 240;
    private const int Height = 160;

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
    private TextOnPath _text;

    [SetUp]
    public void SetUp()
    {
        var device = GpuTestDevice.Device;
        _renderer = new OffscreenTestRenderer(device, new RenderUnitFactory(device, new FontResourceFactory()), Width, Height)
            { ClearColor = Colors.Black };
        // An arch: up from the bottom left, over, down to the bottom right.
        _text = new TextOnPath
        {
            Text = "arch arch",
            FontSize = 24,
            Foreground = Brushes.White,
            Alignment = TextPathAlignment.Center,
            Data = (Geometry)TypeCastFactory.CastFromString("M 30,140 A 90,90 0 0 1 210,140", typeof(Geometry)),
        };
        var canvas = new Canvas();
        canvas.Children.Add(_text);
        _root = new VisualRoot(canvas, Width, Height);
    }

    [TearDown]
    public void TearDown() => _renderer.Dispose();

    private (int Count, int Top, int Bottom) Settled()
    {
        for (var i = 0; i < 4; i++)
        {
            ((IMeasurableComponent)_root).Measure(new Size<double>(Width, Height));
            ((IMeasurableComponent)_root).Arrange(new Rect(0, 0, Width, Height));
            Assert.That(_renderer.RenderFrame(_root), Is.True, "off-screen frame must render");
            RenderDirty.Clear();
        }

        using var img = _renderer.RenderTarget.ResolveTexture.ToImage();
        var pixels = new byte[(int)img.TotalSizeInBytes];
        Marshal.Copy(img.DataPointer, pixels, 0, pixels.Length);
        int count = 0, top = int.MaxValue, bottom = -1;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i] < 64 && pixels[i + 1] < 64 && pixels[i + 2] < 64)
            {
                continue;
            }

            var y = i / 4 / Width;
            count++;
            top = Math.Min(top, y);
            bottom = Math.Max(bottom, y);
        }

        return (count, top, bottom);
    }

    [Test]
    public void TheText_StandsOnTheArch()
    {
        var shown = Settled();

        Assert.Multiple(() =>
        {
            Assert.That(_text.IsTextPlaced, Is.True);
            Assert.That(shown.Count, Is.GreaterThan(100), "letters drawn");
            // The arch tops out at y 50; the letters stand on it and above it, never far below its ends.
            Assert.That(shown.Top, Is.LessThan(50).And.GreaterThan(10));
            Assert.That(shown.Bottom, Is.LessThan(150));
        });
    }

    [Test]
    public void TextLongerThanThePath_IsClipped_ToTheLettersOnIt()
    {
        _text.Text = new string('x', 200);

        var shown = Settled();

        Assert.Multiple(() =>
        {
            Assert.That(_text.IsTextPlaced, Is.True);
            Assert.That(shown.Count, Is.GreaterThan(100));
            Assert.That(shown.Bottom, Is.LessThan(150), "none past the arch's ends");
        });
    }

    [Test]
    public void TextLongerThanThePath_WholeOrNothing_IsNotDrawn()
    {
        _text.Text = new string('x', 200);
        _text.Overflow = TextPathOverflow.Hide;

        var shown = Settled();

        Assert.Multiple(() =>
        {
            Assert.That(_text.IsTextPlaced, Is.False);
            Assert.That(shown.Count, Is.Zero);
        });
    }

    private static Geometry Path(string data) => (Geometry)TypeCastFactory.CastFromString(data, typeof(Geometry));

    [Test]
    public void ALineRunningLeft_IsReadLeftToRight_TheTextAboveIt()
    {
        _text.Data = Path("M 220,80 L 20,80");

        var shown = Settled();

        Assert.Multiple(() =>
        {
            Assert.That(_text.IsTextPlaced, Is.True);
            Assert.That(shown.Top, Is.LessThan(80));
            Assert.That(shown.Bottom, Is.LessThanOrEqualTo(84), "standing on the line, not hanging under it");
        });
    }

    [Test]
    public void ALineRunningLeft_NotKeptUpright_HangsTheTextUnderIt()
    {
        _text.Data = Path("M 220,80 L 20,80");
        _text.KeepUpright = false;

        var shown = Settled();

        Assert.Multiple(() =>
        {
            Assert.That(shown.Top, Is.GreaterThanOrEqualTo(76));
            Assert.That(shown.Bottom, Is.GreaterThan(90));
        });
    }

    [Test]
    public void AcrossOffset_LiftsTheText()
    {
        _text.Data = Path("M 20,100 L 220,100");
        _text.AcrossOffset = 30;

        var shown = Settled();

        Assert.That(shown.Bottom, Is.LessThan(75));
    }

    [Test]
    public void OfTwoFigures_TheTextTakesTheFirst()
    {
        _text.Data = Path("M 20,40 L 220,40 M 20,140 L 220,140");

        var shown = Settled();

        Assert.That(shown.Bottom, Is.LessThan(50));
    }

    [Test]
    public void ACircleOfOneArc_EndingWhereItStarts_TakesTheTextAcrossItsSeam()
    {
        _text.Data = Path("M 120,140 A 60,60 0 1 1 120.01,140");
        _text.StartOffset = 150;

        Settled();

        Assert.That(_text.IsTextPlaced, Is.True);
    }

    [Test]
    public void RoundAClosedFigure_TheTextGoesAcrossItsSeam()
    {
        _text.Data = Path("M 120,140 A 60,60 0 1 1 120.01,140 Z");
        _text.Alignment = TextPathAlignment.Start;
        _text.StartOffset = -40;

        var shown = Settled();

        Assert.Multiple(() =>
        {
            Assert.That(_text.IsTextPlaced, Is.True);
            Assert.That(shown.Count, Is.GreaterThan(100));
        });
    }

    [Test]
    public void AForegroundChanged_RecolorsTheText()
    {
        Settled();
        _text.Foreground = Brushes.Red;
        Settled();

        using var img = _renderer.RenderTarget.ResolveTexture.ToImage();
        var pixels = new byte[(int)img.TotalSizeInBytes];
        Marshal.Copy(img.DataPointer, pixels, 0, pixels.Length);
        int red = 0, white = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var (a, b, c) = (pixels[i], pixels[i + 1], pixels[i + 2]);
            red += Math.Max(a, c) > 128 && b < 64 ? 1 : 0;
            white += a > 200 && b > 200 && c > 200 ? 1 : 0;
        }

        Assert.Multiple(() =>
        {
            Assert.That(red, Is.GreaterThan(50));
            Assert.That(white, Is.Zero);
        });
    }

    [Test]
    public void AClick_LandsOnALetter_NotBesideThePath()
    {
        Settled();

        Assert.Multiple(() =>
        {
            Assert.That(_text.HitTestCore(new Vector2<double>(120, 40)), Is.True, "a letter at the arch's top");
            Assert.That(_text.HitTestCore(new Vector2<double>(120, 100)), Is.False, "inside the arch, under the text");
        });
    }

    [Test]
    public void WithoutAForeground_NothingBreaks()
    {
        _text.Foreground = null;

        Assert.DoesNotThrow(() => Settled());
    }
}
