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

// Slots from a grown transform table start opaque. The first case stays under the boundary to validate the measurement.
[TestFixture]
[Category("Gpu")]
public class TransformSlotGrowthTests
{
    private const int Dim = 200;

    // A rotation makes an element non-axis-aligned, and that is what promotes it to a slot of its OWN (an at-rest
    // element shares its nearest moving ancestor's). N of these therefore claim N slots.
    private static TestControl Rotated(Rect bounds)
    {
        var c = new TestControl
        {
            Bounds = bounds,
            RenderSize = new Size(bounds.Width, bounds.Height),
            RenderTransform = new Transform { RotationAngle = 5 }
        };
        c.RenderAction = s => s.DrawRectangle(Brushes.Red, new Rect(0, 0, bounds.Width, bounds.Height));
        return c;
    }

    private static byte[] RenderRotatedTiles(int count)
    {
        var device = GpuTestDevice.Device;
        var factory = new RenderUnitFactory(device, new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(device, factory, Dim, Dim) { ClearColor = Colors.Transparent };

        var stage = new TestRoot(Dim, Dim);
        for (var i = 0; i < count; i++)
        {
            // All but the LAST are parked off-screen, so the readback speaks about ONE tile: the one holding the highest
            // slot - exactly the one the growth bug emptied.
            var onScreen = i + 1 == count;
            stage.Add(Rotated(onScreen ? new Rect(60, 60, 40, 40) : new Rect(-500, -500, 40, 40)));
        }

        // THREE frames, not one. A slot allocated past the GPU buffer's current size is uploaded by the next frame's
        // catch-up, so a single-frame readback would fail for that reason alone and say nothing about the slot's ALPHA -
        // which is the thing under test, and which no number of frames repairs.
        var root = new VisualRoot(stage, Dim, Dim);
        for (var frame = 0; frame < 3; frame++)
            Assert.That(renderer.RenderFrame(root), Is.True, "off-screen frame must render");

        using var img = renderer.RenderTarget.ResolveTexture.ReadbackToImage();
        var bytes = new byte[(int)img.TotalSizeInBytes];
        Marshal.Copy(img.DataPointer, bytes, 0, bytes.Length);
        return bytes;
    }

    private static int PaintedPixels(byte[] pixels)
    {
        var painted = 0;
        for (var i = 3; i < pixels.Length; i += 4)
            if (pixels[i] > 0) painted++;
        return painted;
    }

    // POSITIVE: comfortably under the table's initial capacity - this always worked.
    [Test]
    public void TileUnderTheSlotBoundaryIsDrawn()
    {
        Assert.That(PaintedPixels(RenderRotatedTiles(16)), Is.GreaterThan(200), "the on-screen tile must be painted");
    }

    // The same tile past the boundary, compared against its under-boundary count so a failure names the growth.
    [Test]
    public void TilePastTheSlotBoundaryIsDrawnTheSame()
    {
        var under = PaintedPixels(RenderRotatedTiles(16));
        var past = PaintedPixels(RenderRotatedTiles(300));

        Assert.That(under, Is.GreaterThan(200), "harness check: the under-boundary tile must paint before anything else is claimed");
        Assert.That(past, Is.EqualTo(under).Within(under * 0.05),
            $"the tile painted {under} px under the slot boundary and {past} px past it - a slot from the grown table " +
            "is not starting OPAQUE, so its element draws fully transparent while still answering the mouse");
    }

    // The unit factory needs one, but nothing here draws a texture or text.
    private sealed class StubResourceFactory : IResourceFactory
    {
        public ITexture CreateTexture(TextureDescription description, byte[] pixelData) => throw new System.NotSupportedException();
        public ITexture CreateTextureArray(TextureDescription description, System.Collections.Generic.IReadOnlyList<byte[]> layers) => throw new System.NotSupportedException();
        public ITexture ImportSharedSurface(SharedSurfaceDescriptor descriptor) => throw new System.NotSupportedException();
        public IRenderTarget CreateRenderTarget(uint width, uint height, MSAALevel msaa, SurfaceFormat format, ImageLayout desiredLayout) => throw new System.NotSupportedException();
        public FontRenderer GetFontRenderer(IGraphicsDevice graphicsDevice) => throw new System.NotSupportedException();
    }
}
