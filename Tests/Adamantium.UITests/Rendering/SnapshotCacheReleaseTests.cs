using System;
using System.Runtime.CompilerServices;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Fonts;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Rendering;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

// The cache every off-screen picture shares draws a different tree each time and throws its units away after each one.
// Whatever it still names of a tree it has finished with lives as long as the application: it kept a map of every group it
// had ever drawn, and a snapshot of a page held that page for good.
[TestFixture]
[Category("Gpu")]
public class SnapshotCacheReleaseTests
{
    private IGraphicsDevice _device;

    [SetUp]
    public void SetUp() => _device = GpuTestDevice.Device;

    [Test]
    public void ATreeDrawnAndDisposed_IsNotKeptByTheCache()
    {
        var factory = new RenderUnitFactory(_device, new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(_device, factory, 32, 32);

        var gone = DrawnAndDisposed(renderer);

        // The next picture is what sweeps the last one's departures - as the next bake would.
        Assert.That(renderer.RenderFrame(Tree()), Is.True);
        renderer.Cache.DisposeUnits();

        for (var i = 0; i < 3 && gone.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.That(gone.IsAlive, Is.False);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference DrawnAndDisposed(OffscreenTestRenderer renderer)
    {
        var root = Tree(out var control);
        Assert.That(renderer.RenderFrame(root), Is.True);
        renderer.Cache.DisposeUnits();

        return new WeakReference(control);
    }

    private static TestRoot Tree() => Tree(out _);

    private static TestRoot Tree(out TestControl control)
    {
        var root = new TestRoot(32, 32);
        control = new TestControl { RenderAction = s => s.DrawRectangle(Brushes.Red, new Rect(0, 0, 8, 8)) };
        control.Bounds = new Rect(0, 0, 8, 8);
        control.RenderSize = new Size<double>(8, 8);
        root.Add(control);
        return root;
    }

    private sealed class StubResourceFactory : IResourceFactory
    {
        public uint MaxTextureDimension => uint.MaxValue;
        public uint MaxTextureArrayLayers => uint.MaxValue;

        public ITexture CreateTexture(TextureDescription description, byte[] pixelData) => throw new NotSupportedException();
        public ITexture CreateTextureArray(TextureDescription description, System.Collections.Generic.IReadOnlyList<byte[]> layers) => throw new NotSupportedException();
        public ITexture ImportSharedSurface(SharedSurfaceDescriptor descriptor) => throw new NotSupportedException();
        public IRenderTarget CreateRenderTarget(uint width, uint height, MSAALevel msaa, SurfaceFormat format, ImageLayout desiredLayout) => throw new NotSupportedException();
        public FontRenderer GetFontRenderer(IGraphicsDevice graphicsDevice) => throw new NotSupportedException();
    }
}
