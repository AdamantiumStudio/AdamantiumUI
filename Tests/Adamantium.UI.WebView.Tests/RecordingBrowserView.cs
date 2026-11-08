using Adamantium.Graphics.Core;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.WebView.Cef;

namespace Adamantium.UI.WebView.Tests;

/// <summary>A view a test makes a browser for: it keeps what the browser tells it.</summary>
internal sealed class RecordingBrowserView : IWebViewBrowserView
{
    /// <summary>The surfaces shown last; null once the browser let go of them.</summary>
    public SharedSurfaceDescriptor Shown { get; private set; }

    /// <summary>The frames written.</summary>
    public int Frames { get; private set; }

    /// <summary>Whether the browser's frames could not be copied from Chromium's texture.</summary>
    public bool Refused { get; private set; }

    /// <summary>The cursor the page asked for last.</summary>
    public Cursor Cursor { get; private set; }

    /// <summary>What the page started dragging last; null before it did.</summary>
    public DataPackage Dragged { get; private set; }

    public void Show(WebViewBrowser browser, SharedSurfaceDescriptor descriptor) => Shown = descriptor;

    public void LetGo(WebViewBrowser browser) => Shown = null;

    public void FrameWritten(WebViewBrowser browser) => Frames++;

    public void FrameRate(WebViewBrowser browser, int rate)
    {
    }

    public void SharedTextureRefused(WebViewBrowser browser) => Refused = true;

    public void CursorChanged(WebViewBrowser browser, Cursor cursor) => Cursor = cursor;

    public void CompositionBoundsChanged(WebViewBrowser browser, Rect bounds)
    {
    }

    public bool StartDragging(WebViewBrowser browser, DataPackage data, DragDropEffects allowed)
    {
        Dragged = data;
        return false;
    }
}
