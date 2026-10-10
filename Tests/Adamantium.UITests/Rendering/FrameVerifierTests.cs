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

// The verifier stays silent on a frame the cache drew right, and writes a report for one it drew wrong.
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

    private static void Draw(OffscreenTestRenderer renderer, VisualRoot root, FrameVerifier verifier)
    {
        Assert.That(renderer.RenderFrame(root), Is.True, "off-screen frame must render");
        RenderDirty.Clear();
        verifier?.FrameEnded(renderer.Cache, renderer.Presenter, renderer.RenderTarget, GpuTestDevice.Device, 1.0);
    }

    [Test]
    public void APatchedMove_IsVerified_AndMatches()
    {
        var device = GpuTestDevice.Device;
        using var renderer = new OffscreenTestRenderer(device, new RenderUnitFactory(device, new StubResourceFactory()), Dim, Dim)
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
        Draw(renderer, root, null);

        using var verifier = new FrameVerifier(_settings, root, device, new StubResourceFactory());
        renderer.Cache.Observer = verifier;

        mover.Bounds = new Rect(48, 8, 24, 24);
        Draw(renderer, root, verifier);

        Assert.That(renderer.Cache.LastFrameReplayed, Is.True, "the move must take the fast path the verifier checks");
        Assert.That(_settings.VerifiedFrames, Is.EqualTo(1), "the patched frame must be compared");
        Assert.That(_settings.MismatchedFrames, Is.Zero, "a frame drawn right must match its full walk");
        Assert.That(_settings.SkippedFrames, Is.Zero);
    }

    // A control that draws differently every time it is asked stands in for a cache holding stale ink: the live frame and
    // the reference walk get different pictures of the same tree.
    [Test]
    public void AFrameThatDiffersFromTheWalk_IsCaught_AndReported()
    {
        var device = GpuTestDevice.Device;
        using var renderer = new OffscreenTestRenderer(device, new RenderUnitFactory(device, new StubResourceFactory()), Dim, Dim)
        {
            ClearColor = Colors.Black
        };

        var stage = Placed(new Rect(0, 0, Dim, Dim));
        var flicker = Placed(new Rect(8, 8, 24, 24));
        var asked = 0;
        flicker.RenderAction = s => s.DrawRectangle(asked++ % 2 == 0 ? Brushes.Blue : Brushes.Red, new Rect(0, 0, 24, 24));
        stage.Add(flicker);
        var root = new VisualRoot(stage, Dim, Dim);
        Draw(renderer, root, null);

        using var verifier = new FrameVerifier(_settings, root, device, new StubResourceFactory());
        renderer.Cache.Observer = verifier;

        flicker.Invalidate();
        Draw(renderer, root, verifier);

        Assert.That(_settings.VerifiedFrames, Is.EqualTo(1));
        Assert.That(_settings.MismatchedFrames, Is.EqualTo(1), "the live frame differs from the walk and must be caught");

        var frames = Directory.GetDirectories(_settings.SessionFolder);
        Assert.That(frames, Has.Length.EqualTo(1), "the differing frame must be written out");
        string[] files = ["report.txt", "live.png", "walk.png", "diff.png", "zoom.png", "live-groups.txt", "walk-groups.txt"];
        foreach (var file in files)
        {
            Assert.That(File.Exists(Path.Combine(frames[0], file)), Is.True, $"{file} must be written");
        }

        var report = File.ReadAllText(Path.Combine(frames[0], "report.txt"));
        Assert.That(report, Does.Contain("drawn by: Patch"), "the report must say how the live frame was drawn");
        Assert.That(report, Does.Contain("dirty (re-recorded or repainted): 1"), "...and what the frame changed");
        Assert.That(report, Does.Contain("live (1):"), "the recolored control is the one suspect");
        Assert.That(File.ReadAllText(Path.Combine(_settings.SessionFolder, "verify.log")), Does.Contain("px differ"));
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
