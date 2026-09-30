using System.Runtime.InteropServices;
using Adamantium.Core;
using Adamantium.Graphics.Core;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Rendering;
using Adamantium.UI.Universes;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

/// <summary>
/// The designer previews every file through ONE renderer, pointed at each new window with <c>Retarget</c>. It went on
/// drawing the previous file: the new tree marked into a scope the cache never read, and a cache whose units were freed
/// still counted as built, so the next record read "nothing changed" and replayed the old scene.
/// </summary>
[TestFixture]
[Category("Gpu")]
public class HeadlessRetargetRenderTests
{
    private const int Dim = 64;

    private sealed class FilledWindow : VirtualWindow
    {
        public Color Fill { get; set; }

        public void Host(IUIComponent child) => AddVisualChild(child);

        protected override void OnRender(IDrawingContext context) =>
            context.ForControl(this).DrawRectangle(new SolidColorBrush(Fill), new Rect(0, 0, Dim, Dim));
    }

    private static FilledWindow Window(Color fill)
    {
        var window = new FilledWindow { Fill = fill, ClientWidth = Dim, ClientHeight = Dim };
        window.Measure(new Size(Dim, Dim));
        window.Arrange(new Rect(0, 0, Dim, Dim));
        return window;
    }

    private static VisualRoot Root(Color fill)
    {
        var content = new TestControl { RenderAction = s => s.DrawRectangle(new SolidColorBrush(fill), new Rect(0, 0, Dim, Dim)) };
        content.Bounds = new Rect(0, 0, Dim, Dim);
        content.RenderSize = new Size(Dim, Dim);
        return new VisualRoot(content, Dim, Dim);
    }

    // One designer frame: record and apply before the pass, draw, then wait so the target can be read back.
    private static void Frame(IGraphicsDevice device, HeadlessWindowRenderer renderer)
    {
        device.ClearColor = Colors.Transparent;
        device.SetRenderTargets(renderer.Presenter.RenderTarget);
        device.SetDepthBuffer(renderer.Presenter.DepthBuffer);
        device.MSAALevel = renderer.Presenter.MSAALevel;
        device.Presenter = renderer.Presenter;
        Assert.That(device.BeginDraw(beforeRenderPass: _ =>
        {
            renderer.PrepareData();
            renderer.PreRender();
        }), Is.True, "the frame must begin");
        renderer.Render(new AppTime());
        device.EndDraw();
        device.Submit();
        device.DeviceWaitIdle();
        device.FrameEnded();
    }

    private static (byte R, byte G, byte B) Pixel(IRenderTarget target) => Pixel(target, Dim, Dim / 2, Dim / 2);

    private static (byte R, byte G, byte B) Pixel(IRenderTarget target, int width, int x, int y)
    {
        using var img = target.ResolveTexture.ReadbackToImage();
        var bytes = new byte[(int)img.TotalSizeInBytes];
        Marshal.Copy(img.DataPointer, bytes, 0, bytes.Length);
        var i = (y * width + x) * 4;
        return (bytes[i + 2], bytes[i + 1], bytes[i]);
    }

    [Test]
    public void AfterItsUnitsAreFreed_TheCacheBuildsTheNextFrame_EvenWithNoMarks()
    {
        var device = GpuTestDevice.Device;
        using var renderer = new OffscreenTestRenderer(device, new RenderUnitFactory(device, new DeviceResourceFactory(device)), Dim, Dim);
        var scope = Adamantium.UI.Core.RenderDirtyRouter.NewScope();
        try
        {
            Assert.That(renderer.RenderFrame(Root(Colors.Red)), Is.True);
            Assume.That(Pixel(renderer.RenderTarget).R, Is.GreaterThan(200), "precondition: the first scene is drawn");

            renderer.Cache.DisposeUnits();
            // The next tree marks into a scope this cache does not read - the designer's case before Retarget claimed one.
            renderer.Cache.Dirty = scope;
            Assert.That(renderer.RenderFrame(Root(Colors.Lime)), Is.True);

            var shown = Pixel(renderer.RenderTarget);
            Assert.That((shown.R, shown.G), Is.EqualTo(((byte)0, (byte)255)), "a cache with no units must build, not replay");
        }
        finally
        {
            Adamantium.UI.Core.RenderDirtyRouter.Forget(scope);
        }
    }

    [Test]
    public void AfterAReset_TheNewWindowIsDrawn_NotTheOneBefore()
    {
        var device = GpuTestDevice.Device;
        var renderer = new HeadlessWindowRenderer(device, new RenderUnitFactory(device, new DeviceResourceFactory(device)));
        try
        {
            renderer.SetWindow(Window(Colors.Red));
            Frame(device, renderer);
            Assume.That(Pixel(renderer.Presenter.RenderTarget).R, Is.GreaterThan(200), "precondition: the first window is drawn");

            renderer.ResetCache();
            renderer.Retarget(Window(Colors.Lime));
            Frame(device, renderer);

            var shown = Pixel(renderer.Presenter.RenderTarget);
            Assert.That((shown.R, shown.G), Is.EqualTo(((byte)0, (byte)255)), "the preview must show the window it was pointed at");
        }
        finally
        {
            renderer.Dispose();
            GpuTestDevice.Reclaim();
        }
    }

    /// <summary>A designer zoom re-renders the same window at another scale. Nothing in the tree changed, so the frame
    /// read as clean and replayed what was baked in the old scale's pixels - the page shrunk into a corner, clipped at its
    /// old size, until a click forced a walk.</summary>
    [Test]
    public void AtANewScale_TheWindowIsDrawnAtThatScale()
    {
        var device = GpuTestDevice.Device;
        var renderer = new HeadlessWindowRenderer(device, new RenderUnitFactory(device, new DeviceResourceFactory(device)));
        try
        {
            // A clip is what is baked in pixels: the geometry itself scales with the viewport.
            var fill = new TestControl
            {
                RenderAction = s => s.DrawRectangle(new SolidColorBrush(Colors.Lime), new Rect(0, 0, Dim, Dim)),
                Bounds = new Rect(0, 0, Dim, Dim),
                RenderSize = new Size(Dim, Dim)
            };
            var clip = new TestControl { ClipToBounds = true, Bounds = new Rect(0, 0, Dim, Dim), RenderSize = new Size(Dim, Dim) };
            clip.Add(fill);
            var window = Window(Colors.Transparent);
            window.Host(clip);
            renderer.SetWindow(window);
            Frame(device, renderer);
            Assume.That(Pixel(renderer.Presenter.RenderTarget).G, Is.EqualTo(255), "precondition: the clipped fill is drawn");

            renderer.RenderScale = 2;
            renderer.Retarget(window);
            Frame(device, renderer);

            var corner = Pixel(renderer.Presenter.RenderTarget, Dim * 2, Dim * 2 - 8, Dim * 2 - 8);
            Assert.That(corner.G, Is.EqualTo(255), "the far corner of the doubled target must be painted too");
        }
        finally
        {
            renderer.Dispose();
            GpuTestDevice.Reclaim();
        }
    }

    /// <summary>The designer builds a fresh tree for every render into the same cache. Freeing the units kept the old
    /// tree's transform slots, so the table grew with every render until one outgrew its buffer mid-frame and drew
    /// everything past the old end in the wrong place.</summary>
    [Test]
    public void FreshTreeAfterFreshTree_IsDrawnInPlace()
    {
        var device = GpuTestDevice.Device;
        var renderer = new HeadlessWindowRenderer(device, new RenderUnitFactory(device, new DeviceResourceFactory(device)));
        try
        {
            renderer.SetWindow(Crowd());
            Frame(device, renderer);

            for (var render = 2; render <= 4; render++)
            {
                renderer.ResetCache();
                renderer.Retarget(Crowd());
                Frame(device, renderer);

                var middle = Pixel(renderer.Presenter.RenderTarget);
                Assert.That((middle.R, middle.G), Is.EqualTo(((byte)0, (byte)255)),
                    $"render {render}: the last element must be drawn where it is");
            }
        }
        finally
        {
            renderer.Dispose();
            GpuTestDevice.Reclaim();
        }
    }

    // Many drawn elements, each holding a transform slot of its own; the last one covers the middle.
    private static FilledWindow Crowd()
    {
        var window = Window(Colors.Transparent);
        for (var i = 0; i < 200; i++)
        {
            var dot = new TestControl
            {
                RenderAction = i == 199
                    ? s => s.DrawRectangle(new SolidColorBrush(Colors.Lime), new Rect(Dim / 4, Dim / 4, Dim / 2, Dim / 2))
                    : s => s.DrawRectangle(new SolidColorBrush(Colors.Red), new Rect(0, 0, 1, 1)),
                Bounds = new Rect(0, 0, Dim, Dim),
                RenderSize = new Size(Dim, Dim)
            };
            window.Host(dot);
        }

        return window;
    }

    [Test]
    public void TheRetargetedWindow_IsDrawn_AndItsEditsReachThePixels()
    {
        var device = GpuTestDevice.Device;
        var renderer = new HeadlessWindowRenderer(device, new RenderUnitFactory(device, new DeviceResourceFactory(device)));
        try
        {
            renderer.SetWindow(Window(Colors.Red));
            Frame(device, renderer);

            var second = Window(Colors.Lime);
            renderer.Retarget(second);
            Frame(device, renderer);
            var retargeted = Pixel(renderer.Presenter.RenderTarget);
            Assert.That((retargeted.R, retargeted.G), Is.EqualTo(((byte)0, (byte)255)), "the preview must show the window it was pointed at");

            second.Fill = Colors.Blue;
            second.InvalidateRender(false);
            Frame(device, renderer);

            var edited = Pixel(renderer.Presenter.RenderTarget);
            Assert.That((edited.G, edited.B), Is.EqualTo(((byte)0, (byte)255)), "an edit to the previewed tree must reach the preview");
        }
        finally
        {
            renderer.Dispose();
            GpuTestDevice.Reclaim();
        }
    }
}
