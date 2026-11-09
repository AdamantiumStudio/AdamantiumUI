using System;
using System.Threading.Tasks;
using Adamantium.Core;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Presentation;
using Adamantium.Imaging;
using Adamantium.UI.Controls;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media.Imaging;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A window's screenshot is the frame its renderer drew, handed over as the renderer reads it back - not a
/// picture drawn again apart from the screen.</summary>
[TestFixture]
public class WindowScreenshotTests
{
    [Test]
    public async Task TheScreenshotIsWhatTheRendererReadsBack()
    {
        var frame = new BitmapSource(2, 1, 1, 1, SurfaceFormat.B8G8R8A8.UNorm, new byte[8]);
        var renderer = new ReadingRenderer();
        var window = new Window { Renderer = renderer };

        var taking = window.TakeScreenshotAsync();
        Assert.That(taking.IsCompleted, Is.False, "the frame is read once the window has drawn it");
        renderer.Deliver(frame);

        Assert.That(await taking, Is.SameAs(frame));
    }

    [Test]
    public async Task AWindowWithNoRendererHasNoScreenshot()
    {
        Assert.That(await new Window().TakeScreenshotAsync(), Is.Null);
    }

    // Holds the one request this is about; everything else a renderer does needs a device and is not the question here.
    private sealed class ReadingRenderer : IWindowRenderer
    {
        private Action<BitmapSource> asked;

        public void RequestScreenshot(Action<BitmapSource> onReady) => asked = onReady;

        public void Deliver(BitmapSource frame) => asked(frame);

        public IDrawingContext DrawingContext => null;
        public bool IsRendererUpToDate => true;
        public bool FirstFrameProcessed => true;
        public long PresentedFrames => 0;
        public bool HasPresentedContent => true;
        public int RecordedFrames => 0;
        public int ChangedFrames => 0;
        public long FrameRequests => 0;
        public void RequestFrame() { }
        public double RenderScale { get; set; } = 1.0;
        public GraphicsPresenter Presenter => null;

        public void InvalidatePresenter() { }
        public void SetWindow(IWindow window) => throw new NotSupportedException();
        public void Retarget(IWindow window) => throw new NotSupportedException();
        public void Render(AppTime appTime) => throw new NotSupportedException();
        public void PreRender() => throw new NotSupportedException();
        public void PrepareData() => throw new NotSupportedException();
        public void RecordData() => throw new NotSupportedException();
        public void ApplyData() => throw new NotSupportedException();
        public void ResetCache() => throw new NotSupportedException();
        public void OnFrameEnded() => throw new NotSupportedException();
        public void Present() => throw new NotSupportedException();
        public void ResizePresenter(PresentationParameters parameters) => throw new NotSupportedException();
        public void ResizePresenter(uint width, uint height) => throw new NotSupportedException();
        public void ResizePresenterToWindow() => throw new NotSupportedException();
        public void Dispose() { }
    }
}
