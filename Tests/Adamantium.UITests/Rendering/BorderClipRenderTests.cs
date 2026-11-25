using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Fonts;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Rendering;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

/// <summary>A rounded border that clips cuts what it holds along the INNER edge of its stroke: a child filling it does
/// not paint over the stroke at the corners, and the stroke itself is not cut.</summary>
[TestFixture]
[Category("Gpu")]
public class BorderClipRenderTests
{
    private const int Dim = 128;
    private const int Radius = 30;
    private const int Stroke = 6;

    private static OffscreenTestRenderer _renderer;

    [OneTimeSetUp]
    public void CreateRenderer()
    {
        var device = GpuTestDevice.Device;
        _renderer = new OffscreenTestRenderer(device, new RenderUnitFactory(device, new StubResourceFactory()), Dim, Dim)
        {
            ClearColor = Colors.Black
        };
    }

    [OneTimeTearDown]
    public void DisposeRenderer()
    {
        _renderer?.Dispose();
        _renderer = null;
    }

    [Test]
    public void AFilledChild_LeavesTheStrokeAtTheCorner()
    {
        var pixels = Render(Root(Box()));

        Assert.Multiple(() =>
        {
            Assert.That(At(pixels, 11, 11), Is.EqualTo("red"), "the middle of the stroke, on the corner");
            Assert.That(At(pixels, 98, 11), Is.EqualTo("red"), "and on the next one");
            Assert.That(At(pixels, 3, 45), Is.EqualTo("red"), "the stroke along the side");
            Assert.That(At(pixels, 17, 17), Is.EqualTo("white"), "what it holds, inside the stroke");
            Assert.That(At(pixels, 2, 2), Is.EqualTo("black"), "nothing outside the corner");
        });
    }

    [Test]
    public void AStrokeGrown_CutsFurtherIn()
    {
        var box = Box();
        var root = Root(box);
        Render(root);

        box.BorderThickness = new Thickness(14);
        var pixels = Render(root);

        // 18 from the corner's center, inside the stroke that is 14 wide now.
        Assert.That(At(pixels, 17, 17), Is.EqualTo("red"));
    }

    // A radius given to the clip itself is what the clip is rounded by, the border's stroke or not.
    [Test]
    public void AClipGivenItsOwnRadius_IsTakenAsGiven()
    {
        var box = Box();
        box.ClipCornerRadius = new CornerRadius(1);
        var pixels = Render(Root(box));

        Assert.That(At(pixels, 11, 11), Is.EqualTo("white"));
    }

    // A fill with no stroke clips at its own edge, as before.
    [Test]
    public void ABorderWithNoStroke_ClipsAtItsEdge()
    {
        var box = Box();
        box.BorderThickness = default;
        var pixels = Render(Root(box));

        Assert.Multiple(() =>
        {
            Assert.That(At(pixels, 11, 11), Is.EqualTo("white"));
            Assert.That(At(pixels, 2, 2), Is.EqualTo("black"));
        });
    }

    // A stroke over the whole of the border leaves nothing inside it: the edge cuts what it holds, as a clip still does.
    [Test]
    public void AStrokeAsWideAsTheBorder_StillClipsAtItsEdge()
    {
        var box = Box();
        box.BorderThickness = new Thickness(60);
        box.Child = new Border { Background = Brushes.White, Margin = new Thickness(-60), Width = 110, Height = 90 };
        var pixels = Render(Root(box));

        Assert.That(At(pixels, 2, 2), Is.EqualTo("black"));
    }

    // Drawn over the stroke, as a focus ring is, an adorner is not cut inside it.
    [Test]
    public void AnAdorner_IsDrawnOverTheStroke()
    {
        var box = Box();
        box.Child = new Adorning { Background = Brushes.White };
        var pixels = Render(Root(box));

        Assert.That(At(pixels, 11, 11), Is.EqualTo("white"));
    }

    // Composed on the border as an adorner is, not held by it.
    private sealed class Adorning : Border
    {
        public override bool ClippedByRenderParent => false;
    }

    // Dropped whole, a cache hands every slot back: none stays another's.
    [Test]
    public void ReleasedSlots_AreNotHeldByAnInnerClip()
    {
        var table = new TransformTable();
        var owner = Guid.NewGuid();
        table.AcquireClipSlot(owner, inner: true);

        table.ReleaseAll();

        Assert.That(table.AcquireClipSlot(owner, inner: true), Is.Not.EqualTo(table.AcquireClipSlot(owner)));
    }

    private static Border Box() => new()
    {
        Width = 110,
        Height = 90,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
        ClipToBounds = true,
        CornerRadius = new CornerRadius(Radius),
        BorderThickness = new Thickness(Stroke),
        BorderBrush = Brushes.Red,
        Child = new Border { Background = Brushes.White }
    };

    private static VisualRoot Root(Border box) => new(box, Dim, Dim);

    private static byte[] Render(VisualRoot root)
    {
        ((IMeasurableComponent)root).Measure(new Size<double>(Dim, Dim));
        ((IMeasurableComponent)root).Arrange(new Rect(0, 0, Dim, Dim));

        RenderDirty.MarkStructural();
        Assert.That(_renderer.RenderFrame(root), Is.True, "off-screen frame must render");
        RenderDirty.Clear();

        using var image = _renderer.RenderTarget.ResolveTexture.ToImage();
        var pixels = new byte[(int)image.TotalSizeInBytes];
        Marshal.Copy(image.DataPointer, pixels, 0, pixels.Length);
        return pixels;
    }

    // The stroke is red and what is held white, so no channel order matters: white has green, the stroke has not.
    private static string At(byte[] pixels, int x, int y)
    {
        var i = (y * Dim + x) * 4;
        var most = Math.Max(pixels[i], Math.Max(pixels[i + 1], pixels[i + 2]));
        return most < 40 ? "black"
            : pixels[i + 1] > 200 ? "white"
            : pixels[i + 1] < 60 ? "red"
            : $"{pixels[i]},{pixels[i + 1]},{pixels[i + 2]}";
    }

    // The unit factory needs one, but nothing here draws a texture or text.
    private sealed class StubResourceFactory : IResourceFactory
    {
        public uint MaxTextureDimension => uint.MaxValue;
        public uint MaxTextureArrayLayers => uint.MaxValue;

        public ITexture CreateTexture(TextureDescription description, byte[] pixelData) => throw new NotSupportedException();
        public ITexture CreateTextureArray(TextureDescription description, IReadOnlyList<byte[]> layers) => throw new NotSupportedException();
        public ITexture ImportSharedSurface(SharedSurfaceDescriptor descriptor) => throw new NotSupportedException();
        public IRenderTarget CreateRenderTarget(uint width, uint height, MSAALevel msaa, SurfaceFormat format, ImageLayout desiredLayout) => throw new NotSupportedException();
        public FontRenderer GetFontRenderer(IGraphicsDevice graphicsDevice) => throw new NotSupportedException();
    }
}
