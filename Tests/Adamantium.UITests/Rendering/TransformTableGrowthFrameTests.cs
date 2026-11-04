using System;
using System.Collections.Generic;
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

// A frame that meets more new elements than the transform table's buffer was sized for at its start - the first view of
// a map full of labels - draws them from slots that buffer does not have unless it grows within the frame: blank text,
// missing fills and triangles stretched across the window, for one frame.
[TestFixture]
[Category("Gpu")]
public class TransformTableGrowthFrameTests
{
    private const int Dim = 96;

    private static TestControl Placed(Rect bounds) =>
        new() { Bounds = bounds, RenderSize = new Size<double>(bounds.Width, bounds.Height) };

    [Test]
    public void ElementsPastTheTablesBuffer_AppearingInOneFrame_AreDrawnFromSlotsThatFrameHas()
    {
        var device = GpuTestDevice.Device;
        using var renderer = new OffscreenTestRenderer(device, new RenderUnitFactory(device, new StubResourceFactory()), Dim, Dim);
        var stage = Placed(new Rect(0, 0, Dim, Dim));
        var root = new VisualRoot(stage, Dim, Dim);
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();

        for (var i = 0; i < 400; i++)
        {
            var cell = Placed(new Rect(i % 20 * 4, i / 20 * 4, 3, 3));
            cell.RenderAction = s => s.DrawRectangle(Brushes.Blue, new Rect(0, 0, 3, 3));
            stage.Add(cell);
        }

        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();

        Assert.That(renderer.Cache.DrawsFromEverySlot, Is.True,
            "the new elements' matrices must be in the buffer the frame that first draws them reads");
    }

    // Rectangles and ellipses in turn break the batches, so draws are recorded before the buffer grows and after it; each
    // half-transparent pair also takes an opacity slot past the buffer.
    [Test]
    public void DrawsRecordedBeforeAndAfterTheGrowth_AndFadingElements_AllDrawFromSlotsThatFrameHas()
    {
        var device = GpuTestDevice.Device;
        using var renderer = new OffscreenTestRenderer(device, new RenderUnitFactory(device, new StubResourceFactory()), Dim, Dim);
        var stage = Placed(new Rect(0, 0, Dim, Dim));
        var root = new VisualRoot(stage, Dim, Dim);
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();

        for (var i = 0; i < 300; i++)
        {
            var pair = Placed(new Rect(i % 20 * 4, i / 20 * 4, 4, 4));
            pair.Opacity = i % 3 == 0 ? 0.5 : 1;
            var square = Placed(new Rect(0, 0, 2, 2));
            square.RenderAction = s => s.DrawRectangle(Brushes.Blue, new Rect(0, 0, 2, 2));
            var dot = Placed(new Rect(2, 2, 2, 2));
            dot.RenderAction = s => s.DrawEllipse(new Rect(0, 0, 2, 2), Brushes.Red, 0, 360,
                Adamantium.ProceduralGeometry.Shapes.EllipseType.Sector);
            pair.Add(square);
            pair.Add(dot);
            stage.Add(pair);
        }

        Assert.That(renderer.RenderFrame(root), Is.True, "the frame that grows the buffer draws");
        RenderDirty.Clear();
        Assert.That(renderer.Cache.DrawsFromEverySlot, Is.True);
        Assert.That(renderer.RenderFrame(root), Is.True, "and so does the one after it");
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
