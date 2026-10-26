using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Fonts;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Rendering;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

// Glyph effects are drawn from the transform table: the glyphs carry where their chain starts, the shaders walk it, so a
// changed effect is a slot write - the text is neither recorded nor packed again.
[TestFixture]
[Category("Gpu")]
public class TextEffectRenderTests
{
    private const int Width = 160;
    private const int Height = 96;

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

    private readonly record struct Lit(int Count, int Left, int Top, int Right, int Bottom, int Red);

    private OffscreenTestRenderer _renderer;
    private VisualRoot _root;
    private Canvas _canvas;
    private TextBlock _block;

    [SetUp]
    public void SetUp()
    {
        var device = GpuTestDevice.Device;
        _renderer = new OffscreenTestRenderer(device, new RenderUnitFactory(device, new FontResourceFactory()), Width, Height)
            { ClearColor = Colors.Black };
        _block = new TextBlock { Text = "abc", Foreground = Brushes.White, FontSize = 24 };
        _canvas = new Canvas();
        Canvas.SetLeft(_block, 40);
        Canvas.SetTop(_block, 30);
        _canvas.Children.Add(_block);
        _root = new VisualRoot(_canvas, Width, Height);
        Draw();
    }

    [TearDown]
    public void TearDown() => _renderer.Dispose();

    private void Draw()
    {
        ((IMeasurableComponent)_root).Measure(new Size(Width, Height));
        ((IMeasurableComponent)_root).Arrange(new Rect(0, 0, Width, Height));
        Assert.That(_renderer.RenderFrame(_root), Is.True, "off-screen frame must render");
        RenderDirty.Clear();
    }

    // Settles glyphs that land in the atlas over the first frames, so what is compared is the effect alone.
    private Lit Settled()
    {
        for (var i = 0; i < 4; i++)
        {
            Draw();
        }

        return Shown();
    }

    private Lit Shown()
    {
        using var img = _renderer.RenderTarget.ResolveTexture.ReadbackToImage();
        var pixels = new byte[(int)img.TotalSizeInBytes];
        Marshal.Copy(img.DataPointer, pixels, 0, pixels.Length);
        int count = 0, red = 0, left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i] < 64 && pixels[i + 1] < 64 && pixels[i + 2] < 64)
            {
                continue;
            }

            var x = i / 4 % Width;
            var y = i / 4 / Width;
            count++;
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x);
            bottom = Math.Max(bottom, y);
            var isRed = Math.Max(pixels[i], pixels[i + 2]) > 3 * pixels[i + 1] + 32;
            red += isRed ? 1 : 0;
        }

        return new Lit(count, left, top, right, bottom, red);
    }

    private TextEffect Move(double x, double y, int start = 0, int count = 3) => new()
    {
        PositionStart = start,
        PositionCount = count,
        TransformOrigin = TextEffectOrigin.Element,
        Transform = new Transform { TranslateX = x, TranslateY = y },
    };

    [Test]
    public void AnEffect_DrawsItsRangeMoved()
    {
        var plain = Settled();
        _block.TextEffects.Add(Move(20, 10));
        var moved = Settled();

        Assert.Multiple(() =>
        {
            Assert.That(plain.Count, Is.GreaterThan(0), "the text is drawn");
            Assert.That(moved.Left - plain.Left, Is.EqualTo(20).Within(1));
            Assert.That(moved.Top - plain.Top, Is.EqualTo(10).Within(1));
            Assert.That(moved.Count, Is.EqualTo(plain.Count).Within(plain.Count / 10), "the same glyphs, elsewhere");
        });
    }

    [Test]
    public void ChangingAnEffect_MovesTheGlyphs_WithoutTheTextRecordedAgain()
    {
        var effect = Move(10, 0);
        _block.TextEffects.Add(effect);
        var before = Settled();

        effect.Transform.TranslateX = 30;
        var stillRecorded = _block.IsGeometryValid;
        Draw();
        var after = Shown();

        Assert.Multiple(() =>
        {
            Assert.That(stillRecorded, Is.True, "only the effect's values changed");
            Assert.That(_renderer.Cache.LastFrameReplayed, Is.True, "the recorded frame is drawn as it was");
            Assert.That(after.Left - before.Left, Is.EqualTo(20).Within(1));
        });
    }

    [Test]
    public void AnEffectsColor_RecolorsItsRange()
    {
        var plain = Settled();
        _block.TextEffects.Add(new TextEffect
        {
            PositionStart = 0,
            PositionCount = 3,
            Foreground = new SolidColorBrush(Colors.Red),
        });
        var red = Settled();

        Assert.Multiple(() =>
        {
            Assert.That(plain.Red, Is.Zero);
            Assert.That(red.Red, Is.GreaterThan(red.Count / 2));
        });
    }

    // In the block's own space: its left half.
    private RectangleGeometry LeftHalf() => new() { Rect = new Rect(0, 0, _block.RenderSize.Width / 2, _block.RenderSize.Height) };

    [Test]
    public void AClip_CutsAwayWhatOfTheRangeIsOutsideIt()
    {
        var plain = Settled();
        _block.TextEffects.Add(new TextEffect { PositionStart = 0, PositionCount = 3, Clip = LeftHalf() });
        var cut = Settled();
        var edge = 40 + _block.RenderSize.Width / 2;

        Assert.Multiple(() =>
        {
            Assert.That(cut.Left, Is.EqualTo(plain.Left).Within(1));
            Assert.That(cut.Right, Is.LessThanOrEqualTo(edge + 1), "nothing right of the clip");
            Assert.That(cut.Count, Is.LessThan(plain.Count * 3 / 4));
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public void AClip_GoesWithTheCharacters_OrStays(bool follows)
    {
        var plain = Settled();
        var effect = Move(80, 0);
        effect.Clip = LeftHalf();
        effect.ClipFollowsTransform = follows;
        _block.TextEffects.Add(effect);
        var shown = Settled();

        if (follows)
        {
            Assert.That(shown.Left - plain.Left, Is.EqualTo(80).Within(1), "the left half, moved along");
        }
        else
        {
            Assert.That(shown.Count, Is.Zero, "moved out of a clip that stayed");
        }
    }

    [Test]
    public void AClipGoingWithTheCharacters_CutsThemWhereTheLayoutPutThem_UnderAnEarlierEffect()
    {
        var plain = Settled();
        _block.TextEffects.Add(Move(80, 0));
        _block.TextEffects.Add(new TextEffect { PositionStart = 0, PositionCount = 3, Clip = LeftHalf() });
        var shown = Settled();

        Assert.Multiple(() =>
        {
            Assert.That(shown.Left - plain.Left, Is.EqualTo(80).Within(1), "the left half, moved by the first effect");
            Assert.That(shown.Count, Is.LessThan(plain.Count * 3 / 4).And.GreaterThan(0));
        });
    }

    [Test]
    public void AnimatingAClip_RedrawsWithoutTheTextRecordedAgain()
    {
        var clip = LeftHalf();
        _block.TextEffects.Add(new TextEffect { PositionStart = 0, PositionCount = 3, Clip = clip });
        var half = Settled();

        clip.Rect = new Rect(0, 0, _block.RenderSize.Width, _block.RenderSize.Height);
        var stillRecorded = _block.IsGeometryValid;
        Draw();
        var whole = Shown();

        Assert.Multiple(() =>
        {
            Assert.That(stillRecorded, Is.True);
            Assert.That(whole.Right, Is.GreaterThan(half.Right + 5), "the clip opened");
        });
    }

    [Test]
    public void ASecondEffect_JoinsTheChain_WithoutTheTextRecordedAgain()
    {
        var first = Move(0, 0, 0, 1);
        first.Transform.TranslateY = -8;
        _block.TextEffects.Add(first);
        var one = Settled();

        _block.TextEffects.Add(Move(0, 8, 1, 2));
        var stillRecorded = _block.IsGeometryValid;
        var two = Settled();

        Assert.Multiple(() =>
        {
            Assert.That(stillRecorded, Is.True, "an effect more is a slot more");
            Assert.That(two.Bottom - one.Bottom, Is.EqualTo(8).Within(1), "the rest of the text went down");
            Assert.That(two.Top, Is.EqualTo(one.Top).Within(1), "the first letter is still up");
        });
    }

    [Test]
    public void ATextDrawnByItself_TakesItsEffectsToo()
    {
        // Turned a quarter, the block leaves the batch and draws on its own.
        _block.RenderTransform = new Transform { RotationAngle = 90 };
        var plain = Settled();
        _block.TextEffects.Add(Move(20, 0));
        var moved = Settled();

        Assert.Multiple(() =>
        {
            Assert.That(plain.Count, Is.GreaterThan(0), "the turned text is drawn");
            Assert.That(moved.Top - plain.Top, Is.EqualTo(20).Within(1), "along the text, which now runs down");
            Assert.That(moved.Left, Is.EqualTo(plain.Left).Within(1));
        });
    }

    // Against frames that move a motion node instead: a frame redrawn from the recorded stream after a few table writes
    // either way, so what is left is the effect's own.
    [Test]
    public void AnEffectAnimatedEveryFrame_AllocatesNoMoreThanAMovedNode()
    {
        var effect = Move(0, 0);
        _block.TextEffects.Add(effect);
        var slide = new Transform();
        var node = new Border { Width = 4, Height = 4, Background = Brushes.Blue, RenderTransform = slide, IsRenderMotionNode = true };
        _canvas.Children.Add(node);
        Settled();

        const int frames = 200;
        long Frames(Action<int> change)
        {
            for (var i = 0; i < 30; i++)
            {
                change(i);
                Draw();
            }

            var start = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < frames; i++)
            {
                change(i);
                Draw();
            }

            return (GC.GetAllocatedBytesForCurrentThread() - start) / frames;
        }

        var moved = Frames(i => slide.TranslateY = i % 5 + 0.5);
        var animated = Frames(i => effect.Transform.TranslateY = i % 5 + 0.5);

        TestContext.Out.WriteLine($"moved node {moved} B/frame, animated effect {animated} B/frame");
        Assert.That(animated, Is.LessThanOrEqualTo(moved));
    }
}
