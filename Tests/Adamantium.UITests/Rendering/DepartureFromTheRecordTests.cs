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

// Who left the tree is the record's call. The render thread applies a packet while the loop is already changing the tree
// again: a control moved between two parents is, for that moment, in neither - and an applier that asked the live tree
// dropped it, and froze its place from the tree it found.
[TestFixture]
[Category("Gpu")]
public class DepartureFromTheRecordTests
{
    private const int Dim = 96;

    private static TestControl Placed(Rect bounds) =>
        new() { Bounds = bounds, RenderSize = new Size(bounds.Width, bounds.Height) };

    private static (byte R, byte G, byte B) Pixel(OffscreenTestRenderer renderer, int x, int y)
    {
        using var img = renderer.RenderTarget.ResolveTexture.ReadbackToImage();
        var bytes = new byte[(int)img.TotalSizeInBytes];
        Marshal.Copy(img.DataPointer, bytes, 0, bytes.Length);
        var at = (y * Dim + x) * 4;
        return (bytes[at + 2], bytes[at + 1], bytes[at]);
    }

    [Test]
    public void AControlTheRecordSawIsDrawn_ThoughTheTreeHasMovedItOnSince()
    {
        var device = GpuTestDevice.Device;
        using var renderer = new OffscreenTestRenderer(device, new RenderUnitFactory(device, new StubResourceFactory()), Dim, Dim)
        {
            ClearColor = Colors.Black
        };

        var stage = Placed(new Rect(0, 0, Dim, Dim));
        var left = Placed(new Rect(0, 0, 48, Dim));
        var right = Placed(new Rect(48, 0, 48, Dim));
        stage.Add(left);
        stage.Add(right);

        var mover = Placed(new Rect(8, 8, 24, 24));
        mover.RenderAction = s => s.DrawRectangle(Brushes.Red, new Rect(0, 0, 24, 24));
        left.Add(mover);

        var root = new VisualRoot(stage, Dim, Dim);
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();

        // The move: out of one layer and into the next, as a canvas restacking its layers does.
        left.Remove(mover);
        right.Add(mover);
        renderer.Cache.RecordFrame(root);

        // The loop is already on its next change while the render thread applies the packet recorded above.
        right.Remove(mover);
        renderer.Cache.ApplyFrame();
        Assert.That(renderer.RenderAgain(root), Is.True);

        var drawn = Pixel(renderer, 48 + 8 + 12, 8 + 12);
        Assert.That(drawn.R, Is.GreaterThan(200), $"the frame the record described has the control in the right layer: {drawn}");

        // The departure is the next record's to report, and that one frees it.
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();
        var gone = Pixel(renderer, 48 + 8 + 12, 8 + 12);
        Assert.That(gone.R, Is.LessThan(60), $"the next frame must not draw a control that has left: {gone}");

        // ...and once back, it is drawn again, from a layout the record sends anew.
        right.Add(mover);
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();
        var back = Pixel(renderer, 48 + 8 + 12, 8 + 12);
        Assert.That(back.R, Is.GreaterThan(200), $"a control that came back must be drawn where it stands: {back}");
    }

    // The unit factory needs one, but nothing here draws a texture or text.
    private sealed class StubResourceFactory : IResourceFactory
    {
        public ITexture CreateTexture(TextureDescription description, byte[] pixelData) => throw new NotSupportedException();
        public ITexture CreateTextureArray(TextureDescription description, IReadOnlyList<byte[]> layers) => throw new NotSupportedException();
        public ITexture ImportSharedSurface(SharedSurfaceDescriptor descriptor) => throw new NotSupportedException();
        public IRenderTarget CreateRenderTarget(uint width, uint height, MSAALevel msaa, SurfaceFormat format, ImageLayout desiredLayout) => throw new NotSupportedException();
        public FontRenderer GetFontRenderer(IGraphicsDevice graphicsDevice) => throw new NotSupportedException();
    }
}
