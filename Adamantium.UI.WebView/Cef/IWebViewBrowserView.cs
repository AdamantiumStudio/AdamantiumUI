using Adamantium.Graphics.Core;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.WebView.Cef;

/// <summary>What a <see cref="WebViewBrowser"/> tells the view it draws for; each call names the browser, so a view that
/// has made another since leaves the old one's calls alone.</summary>
internal interface IWebViewBrowserView
{
    /// <summary>The surfaces the browser's frames are written to from now on.</summary>
    void Show(WebViewBrowser browser, SharedSurfaceDescriptor descriptor);

    /// <summary>The browser lets go of its surfaces: the view is to stop showing them first.</summary>
    void LetGo(WebViewBrowser browser);

    /// <summary>A frame was written: the window is to draw again.</summary>
    void FrameWritten(WebViewBrowser browser);

    /// <summary>The frames written in the last second.</summary>
    void FrameRate(WebViewBrowser browser, int rate);

    /// <summary>The browser's frames cannot be copied from Chromium's texture: the view is to make it again on pixels.
    /// </summary>
    void SharedTextureRefused(WebViewBrowser browser);

    /// <summary>The cursor the page asks for over the view.</summary>
    void CursorChanged(WebViewBrowser browser, Cursor cursor);

    /// <summary>The page starts dragging <paramref name="data"/>, allowing <paramref name="allowed"/>: false when the
    /// view does not take the drag, which the page then drops.</summary>
    bool StartDragging(WebViewBrowser browser, DataPackage data, DragDropEffects allowed);

    /// <summary>Where the text an input method composes stands in the view, in its units: the input method's windows go
    /// beside it.</summary>
    void CompositionBoundsChanged(WebViewBrowser browser, Rect bounds);
}
