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

// A clean frame's op stream is recorded into a secondary command buffer and executed from it while a fingerprint of the
// stream says it would issue the same draws. What is read live while issuing - here the noise clock - must stop it.
[TestFixture]
[Category("Gpu")]
public class RecordedReplayRenderTests
{
    private const int Dim = 64;

    private sealed class Scene : IDisposable
    {
        public OffscreenTestRenderer Renderer;
        public VisualRoot Root;
        public TestControl Stage;
        public TestControl Fills;
        public SolidColorBrush Fill;
        public SolidColorBrush Ink;

        public void Draw()
        {
            Assert.That(Renderer.RenderFrame(Root), Is.True, "off-screen frame must render");
            RenderDirty.Clear();
        }

        public RecordedReplay Replay => Renderer.Cache.LastRecordedReplay;

        public void Dispose() => Renderer.Dispose();
    }

    private static TestControl Placed(Rect bounds) =>
        new() { Bounds = bounds, RenderSize = new Size(bounds.Width, bounds.Height) };

    // Batched fills, a noise fill that reads the clock, and a line drawn by itself.
    private static Scene NewScene()
    {
        var device = GpuTestDevice.Device;
        Assume.That(device.SupportsRecordedCommands, "the device cannot record secondary command buffers");

        var renderer = new OffscreenTestRenderer(device, new RenderUnitFactory(device, new StubResourceFactory()), Dim, Dim)
        {
            ClearColor = Colors.Black
        };
        var ink = new SolidColorBrush(Colors.White);
        var fill = new SolidColorBrush(Colors.Blue);
        var stage = Placed(new Rect(0, 0, Dim, Dim));
        var fills = Placed(new Rect(0, 0, Dim, Dim));
        fills.RenderAction = s => s
            .DrawRectangle(fill, new Rect(4, 4, 24, 24))
            .DrawRectangle(new NoiseBrush(), new Rect(36, 4, 24, 24))
            .DrawLine(new Vector2(4, 48), new Vector2(60, 56), new Pen(ink, 3));
        stage.Add(fills);

        var scene = new Scene { Renderer = renderer, Root = new VisualRoot(stage, Dim, Dim), Stage = stage, Fills = fills, Fill = fill, Ink = ink };
        scene.Draw();
        return scene;
    }

    private static byte[] Pixels(OffscreenTestRenderer renderer)
    {
        using var img = renderer.RenderTarget.ResolveTexture.ReadbackToImage();
        var bytes = new byte[(int)img.TotalSizeInBytes];
        Marshal.Copy(img.DataPointer, bytes, 0, bytes.Length);
        return bytes;
    }

    private static int DifferingPixels(byte[] a, byte[] b)
    {
        var count = 0;
        for (var i = 0; i < a.Length; i += 4)
        {
            if (a[i] != b[i] || a[i + 1] != b[i + 1] || a[i + 2] != b[i + 2] || a[i + 3] != b[i + 3]) count++;
        }

        return count;
    }

    // Each frame slot records once, then executes; a few clean frames are enough to see every slot do both.
    private static void DrawUntilExecuted(Scene scene)
    {
        for (var i = 0; i < 8 && scene.Replay != RecordedReplay.Executed; i++)
        {
            scene.Draw();
        }

        Assert.That(scene.Replay, Is.EqualTo(RecordedReplay.Executed), "a clean scene must come to be executed from a recording");
    }

    private static byte[] IssuedPixels(Scene scene)
    {
        RenderCache.RecordedReplayDisabled = true;
        try
        {
            scene.Draw();
            Assert.That(scene.Replay, Is.EqualTo(RecordedReplay.Issued));
            return Pixels(scene.Renderer);
        }
        finally
        {
            RenderCache.RecordedReplayDisabled = false;
        }
    }

    [Test]
    public void ACleanScene_IsExecutedFromItsRecording_AndDrawsWhatIssuingDraws()
    {
        using var scene = NewScene();

        DrawUntilExecuted(scene);
        var executed = Pixels(scene.Renderer);

        Assert.That(DifferingPixels(executed, IssuedPixels(scene)), Is.Zero,
            "executing the recording must draw exactly what issuing the stream draws");
    }

    [Test]
    public void AClockReadWhileIssuing_KeepsTheOldRecordingFromBeingExecuted()
    {
        using var scene = NewScene();
        DrawUntilExecuted(scene);

        var clock = typeof(NoiseClock).GetProperty(nameof(NoiseClock.Time))!;
        var was = NoiseClock.Time;
        clock.SetValue(null, was + 1.5);
        try
        {
            scene.Draw();

            Assert.That(scene.Renderer.Cache.LastFrameReplayed, Is.True, "precondition: nothing marked the scene dirty");
            Assert.That(scene.Replay, Is.EqualTo(RecordedReplay.Recorded),
                "the noise fill's constants changed, so the frame must be recorded again, not executed from the old one");
        }
        finally
        {
            clock.SetValue(null, was);
        }
    }

    [Test]
    public void ARecoloredPen_IsDrawnInItsNewColor()
    {
        using var scene = NewScene();
        DrawUntilExecuted(scene);
        var white = Pixels(scene.Renderer);

        scene.Ink.Color = Colors.Red;
        scene.Draw();
        var red = Pixels(scene.Renderer);

        Assert.That(scene.Renderer.Cache.LastFrameReplayed, Is.True, "precondition: the recolor reaches a replayed frame");
        Assert.That(scene.Replay, Is.Not.EqualTo(RecordedReplay.Executed), "the old recording draws the old color");
        Assert.That(DifferingPixels(white, red), Is.Not.Zero, "the line must change color");
        Assert.That(DifferingPixels(red, IssuedPixels(scene)), Is.Zero, "and draw what issuing the stream draws");
    }

    // A patch written exactly a lap of frame slots after the walk must still move to another copy: the frame before it,
    // in flight, reads the copy the walk wrote. Counting frames by slot index took the lap for the same frame.
    [Test]
    public void APatchAFullLapAfterTheWalk_WritesAnotherCopy()
    {
        using var scene = NewScene();
        var device = GpuTestDevice.Device;
        Assume.That(device.MaxFramesInFlight, Is.GreaterThanOrEqualTo(2), "a ring of one copy has nothing to choose");
        var batch = scene.Renderer.Cache.RectBatch;
        Assume.That(batch, Is.Not.Null, "precondition: the scene batches its rectangles");
        var walked = batch.CurrentCopy;

        for (var i = 1; i < device.MaxFramesInFlight; i++)
        {
            scene.Draw();
            Assert.That(batch.CurrentCopy, Is.EqualTo(walked), "a clean frame writes nothing");
        }

        scene.Fill.Color = Colors.Green;
        scene.Draw();

        Assert.That(scene.Renderer.Cache.LastFrameReplayed, Is.True, "precondition: the recolor is a patch, not a walk");
        Assert.That(batch.CurrentCopy, Is.Not.EqualTo(walked), "the patch must not write the copy a frame in flight reads");
    }

    // A frame that patches and then walks takes ONE copy: a second would be the one the frame before it is reading.
    [Test]
    public void APatchThenAWalkInOneFrame_TakeOneCopy()
    {
        using var scene = NewScene();
        var device = GpuTestDevice.Device;
        Assume.That(device.MaxFramesInFlight, Is.GreaterThanOrEqualTo(2), "a ring of one copy has nothing to choose");
        var batch = scene.Renderer.Cache.RectBatch;
        var walked = batch.CurrentCopy;

        scene.Fill.Color = Colors.Green;
        RenderDirty.MarkStructural();
        scene.Draw();

        Assert.That(scene.Renderer.Cache.LastFrameReplayed, Is.False, "precondition: the frame walks");
        Assert.That(batch.CurrentCopy, Is.EqualTo((walked + 1) % (int)device.MaxFramesInFlight),
            "the patch and the walk of one frame share the copy the frame took");
    }

    // The vector fills patched in place: a recolor on a clean frame wrote the copy the frames since the walk draw from.
    [Test]
    public void AShapeRecoloredOnACleanFrame_WritesAnotherCopy()
    {
        using var scene = NewScene();
        var device = GpuTestDevice.Device;
        Assume.That(device.MaxFramesInFlight, Is.GreaterThanOrEqualTo(2), "a ring of one copy has nothing to choose");
        var paint = new SolidColorBrush(Colors.Yellow);
        var shape = Placed(new Rect(0, 0, Dim, Dim));
        shape.RenderAction = s => s.DrawGeometry(paint, new EllipseGeometry(new Rect(30, 30, 30, 30)));
        scene.Stage.Add(shape);
        RenderDirty.MarkStructural();
        scene.Draw();
        var fills = scene.Renderer.Cache.InstancedFill;
        Assume.That(fills, Is.Not.Null, "precondition: the shape is an instanced fill");
        var walked = fills.CurrentCopy;
        scene.Draw();

        paint.Color = Colors.Magenta;
        scene.Draw();

        Assert.That(scene.Renderer.Cache.LastFrameReplayed, Is.True, "precondition: the recolor is a patch, not a walk");
        Assert.That(fills.CurrentCopy, Is.Not.EqualTo(walked), "the patch must not write the copy a frame in flight reads");
        Assert.That(DifferingPixels(Pixels(scene.Renderer), IssuedPixels(scene)), Is.Zero,
            "and the copy it moved to holds the whole scene");
    }

    // A backdrop material on a tessellated shape copies the frame out of the render pass, which a recording cannot hold.
    [Test]
    public void AMaterialOnAShape_IsIssued_NotRecorded()
    {
        using var scene = NewScene();
        var glass = Placed(new Rect(0, 0, Dim, Dim));
        glass.RenderAction = s => s.DrawGeometry(new MaterialBrush(), new EllipseGeometry(new Rect(8, 8, 40, 40)));
        scene.Stage.Add(glass);
        RenderDirty.MarkStructural();
        scene.Draw();

        for (var i = 0; i < 6; i++)
        {
            scene.Draw();
            Assert.That(scene.Replay, Is.EqualTo(RecordedReplay.Issued), $"clean frame {i}");
        }
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
