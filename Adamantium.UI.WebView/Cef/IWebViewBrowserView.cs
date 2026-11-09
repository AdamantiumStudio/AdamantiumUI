using Adamantium.Graphics.Core;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Media.Imaging;

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

    /// <summary>The page's address, after it went somewhere of its own - a link, a redirect, back or forward.</summary>
    void AddressChanged(WebViewBrowser browser, string address);

    /// <summary>The page's title.</summary>
    void TitleChanged(WebViewBrowser browser, string title);

    /// <summary>Whether the page is loading, and whether it can go back and forward.</summary>
    void LoadingChanged(WebViewBrowser browser, bool isLoading, bool canGoBack, bool canGoForward);

    /// <summary>How far the page has loaded, 0 to 1.</summary>
    void ProgressChanged(WebViewBrowser browser, double progress);

    /// <summary>What the page puts in a status line: the address of the link under the pointer; empty for none.</summary>
    void StatusChanged(WebViewBrowser browser, string status);

    /// <summary>The page is about to go to <paramref name="address"/> in its main frame: true keeps it where it is.
    /// </summary>
    bool NavigationStarting(WebViewBrowser browser, string address, bool userGesture, bool isRedirect);

    /// <summary>The page at <paramref name="address"/> loaded in the main frame, or failed to: <paramref name="error"/>
    /// says why.</summary>
    void NavigationCompleted(WebViewBrowser browser, string address, WebViewLoadError error);

    /// <summary>The page sent <paramref name="json"/> with <c>window.adamantium.postMessage</c>.</summary>
    void MessageReceived(WebViewBrowser browser, string json);

    /// <summary>The page's zoom, as Chromium keeps it for its site, once a page has loaded.</summary>
    void ZoomChanged(WebViewBrowser browser, double factor);

    /// <summary>The page failed to load; null when one starts loading again.</summary>
    void LoadFailed(WebViewBrowser browser, WebViewLoadError error);

    /// <summary>Where the text an input method composes stands in the view, in its units: the input method's windows go
    /// beside it.</summary>
    void CompositionBoundsChanged(WebViewBrowser browser, Rect bounds);

    /// <summary>The page's icon, shown at the screen's scale; null for none or not yet.</summary>
    void IconChanged(WebViewBrowser browser, ImageSource icon);

    /// <summary>Whether the page came over a connection with nothing wrong.</summary>
    void SecurityChanged(WebViewBrowser browser, bool isSecure);

    /// <summary>The tooltip the page wants for what is under the pointer; empty for none.</summary>
    void TooltipChanged(WebViewBrowser browser, string text);

    /// <summary>Shows the menu the page asks for and answers it once the user picks a row or lets it go; false leaves it
    /// to be answered with none.</summary>
    bool ContextMenuRequested(WebViewBrowser browser, WebViewContextMenu menu);
}
