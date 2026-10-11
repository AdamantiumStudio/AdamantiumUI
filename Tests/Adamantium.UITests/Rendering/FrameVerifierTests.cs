using System;
using System.Collections.Generic;
using System.IO;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Fonts;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Rendering;
using Adamantium.UI.Rendering.Verification;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

// The verifier stays silent while the cache draws with what the tree says, and names what parted when it does not.
[TestFixture]
[Category("Gpu")]
public class FrameVerifierTests
{
    private const int Dim = 96;

    private string _folder;
    private FrameVerification _settings;

    [SetUp]
    public void SwitchOn()
    {
        _folder = Path.Combine(Path.GetTempPath(), "verify-tests", Guid.NewGuid().ToString("N"));
        _settings = new FrameVerification { Folder = _folder, IsEnabled = true };
    }

    [TearDown]
    public void SwitchOff()
    {
        _settings.IsEnabled = false;
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, true);
        }
    }

    private static TestControl Placed(Rect bounds) =>
        new() { Bounds = bounds, RenderSize = new Size(bounds.Width, bounds.Height) };

    private static (OffscreenTestRenderer Renderer, VisualRoot Root, TestControl Mover) Scene()
    {
        var device = GpuTestDevice.Device;
        var renderer = new OffscreenTestRenderer(device, new RenderUnitFactory(device, new StubResourceFactory()), Dim, Dim)
        {
            ClearColor = Colors.Black
        };

        var stage = Placed(new Rect(0, 0, Dim, Dim));
        var still = Placed(new Rect(0, 60, Dim, 20));
        still.RenderAction = s => s.DrawRectangle(Brushes.Green, new Rect(0, 0, Dim, 20));
        stage.Add(still);
        var mover = Placed(new Rect(8, 8, 24, 24));
        mover.RenderAction = s => s.DrawRectangle(Brushes.Blue, new Rect(0, 0, 24, 24));
        stage.Add(mover);

        var root = new VisualRoot(stage, Dim, Dim);
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();
        return (renderer, root, mover);
    }

    private static void Draw(OffscreenTestRenderer renderer, VisualRoot root, FrameVerifier verifier)
    {
        Assert.That(renderer.RenderFrame(root), Is.True, "off-screen frame must render");
        RenderDirty.Clear();
        verifier.FrameDrawn(renderer.Cache);
    }

    [Test]
    public void APatchedMove_IsChecked_AndRaisesNothing()
    {
        var (renderer, root, mover) = Scene();
        using var _ = renderer;
        var verifier = new FrameVerifier(_settings, root);
        renderer.Cache.Observer = verifier;

        mover.Bounds = new Rect(48, 8, 24, 24);
        Draw(renderer, root, verifier);

        Assert.That(renderer.Cache.LastFrameReplayed, Is.True, "the move must take the fast path");
        Assert.That(_settings.VerifiedFrames, Is.EqualTo(1), "the frame must be checked");
        Assert.That(_settings.MismatchedFrames, Is.Zero, "a frame drawn with what the tree says raises nothing");
    }

    // The trace the canvas defect left: a control's frozen layout at the origin with no parent, while the tree has it in
    // its place.
    [Test]
    public void AFrozenLayoutThatPartedFromTheTree_IsCaught_AndNamed()
    {
        var (renderer, root, mover) = Scene();
        using var _ = renderer;
        var verifier = new FrameVerifier(_settings, root);
        renderer.Cache.Observer = verifier;

        mover.Bounds = new Rect(48, 8, 24, 24);
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();
        renderer.Cache.RebaseToOrigin(mover);
        verifier.FrameDrawn(renderer.Cache);

        Assert.That(_settings.VerifiedFrames, Is.EqualTo(1));
        Assert.That(_settings.MismatchedFrames, Is.EqualTo(1), "the cache holds a place the tree does not have");

        var log = File.ReadAllText(_settings.LogPath);
        Assert.That(log, Does.Contain("TestControl"), "the alarm names the component");
        Assert.That(log, Does.Contain("offset in the cache (0, 0), in the tree (48, 8)"), "...the field, and both values");
        Assert.That(log, Does.Contain("parent in the cache (none)"), "...and the parent it lost");
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
