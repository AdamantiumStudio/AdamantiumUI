using System;
using Adamantium.Core;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Presentation;
using Adamantium.UI.Controls;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using NUnit.Framework;

namespace Adamantium.UITests;

// Toggling transparency marks the presenter stale; the frame loop rebuilds the swapchain at the next boundary, as for a
// resize.
[TestFixture]
public class WindowPresenterInvalidationTests
{
    [Test]
    public void TogglingTransparentComposition_MarksThePresenterStale()
    {
        var window = new Window();
        var renderer = new RecordingRenderer();
        window.Renderer = renderer;

        window.UseTransparentComposition = true;

        Assert.That(renderer.Invalidations, Is.EqualTo(1),
            "the window asked for a rebuild - and asked for nothing else, because a rebuild is not the setter's to do");
    }

    /// <summary>Setting it to what it already is changes nothing, so it must not cost a swapchain. Rebuilding one is a
    /// device-idle wait plus every render target - not something to spend on a write that said nothing.</summary>
    [Test]
    public void WritingTheSameValue_CostsNothing()
    {
        var window = new Window { UseTransparentComposition = true };
        var renderer = new RecordingRenderer();
        window.Renderer = renderer;

        window.UseTransparentComposition = true;

        Assert.That(renderer.Invalidations, Is.Zero);
    }

    // Counts the one call this is about; everything else a renderer does needs a device and is not the question here.
    private sealed class RecordingRenderer : IWindowRenderer
    {
        public int Invalidations { get; private set; }

        public void InvalidatePresenter() => Invalidations++;

        public IDrawingContext DrawingContext => null;
        public bool IsRendererUpToDate => true;
        public bool FirstFrameProcessed => true;
        public double RenderScale { get; set; } = 1.0;
        public GraphicsPresenter Presenter => null;

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
        public void Dispose() { }
    }
}
