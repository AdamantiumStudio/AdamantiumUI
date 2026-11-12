using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Fonts;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Rendering;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

/// <summary>A stroke runs along its geometry's curves, so however far it is zoomed in its line keeps to the curve: a
/// small circle blown up 28 times is stroked round, where the polyline it was once cut into - a handful of points for a
/// circle that small - would show its corners pixels off the circle.</summary>
[TestFixture]
[Category("Gpu")]
public class CurveStrokeRenderTests
{
    private const int Dim = 320;

    private static byte[] Render(Geometry geometry, Pen pen, Transform transform)
    {
        var device = GpuTestDevice.Device;
        var factory = new RenderUnitFactory(device, new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(device, factory, Dim, Dim) { ClearColor = Colors.Transparent };
        var content = new TestControl { RenderAction = s => s.DrawGeometry(null, geometry, pen) };
        content.Bounds = new Rect(0, 0, Dim, Dim);
        content.RenderSize = new Size<double>(Dim, Dim);
        if (transform != null) content.RenderTransform = transform;

        Assert.That(renderer.RenderFrame(new VisualRoot(content, Dim, Dim)), Is.True);
        using var img = renderer.RenderTarget.ResolveTexture.ToImage();
        var bytes = new byte[(int)img.TotalSizeInBytes];
        Marshal.Copy(img.DataPointer, bytes, 0, bytes.Length);
        return bytes;
    }

    // Every pixel well inside the line is covered and every one well clear of it is not: the line, 6 px wide, is the
    // points 3 px either side of a circle of radius 140 about the middle.
    private static void AssertARing(byte[] pixels)
    {
        var holes = 0;
        var strays = 0;
        for (var y = 0; y < Dim; y++)
        for (var x = 0; x < Dim; x++)
        {
            var off = Math.Abs(Math.Sqrt((x + 0.5 - 160) * (x + 0.5 - 160) + (y + 0.5 - 160) * (y + 0.5 - 160)) - 140);
            var alpha = pixels[(y * Dim + x) * 4 + 3];
            if (off < 1.5 && alpha < 250) holes++;
            if (off > 4.5 && alpha > 5) strays++;
        }

        Assert.Multiple(() =>
        {
            Assert.That(holes, Is.Zero, "the line covers the circle all round");
            Assert.That(strays, Is.Zero, "and nothing off it");
        });
    }

    [Test]
    public void ACircle_IsStrokedRound()
    {
        var circle = new EllipseGeometry(new Vector2<double>(160, 160), 140, 140);

        AssertARing(Render(circle, new Pen(Brushes.Black, 6), null));
    }

    [Test]
    public void ASmallCircleZoomedIn_IsStrokedRound()
    {
        var circle = new EllipseGeometry(new Vector2<double>(5, 5), 5, 5);
        var zoom = new Transform { ScaleX = 28, ScaleY = 28, TranslateX = 20, TranslateY = 20 };

        AssertARing(Render(circle, new Pen(Brushes.Black, 6.0 / 28), zoom));
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
