using Adamantium.Graphics.Core;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Media.Imaging;

namespace Adamantium.UI.WebView.Cef;

/// <summary>The view of a page shown nowhere - the blank page <see cref="WebViewEngine.StartAhead"/> keeps Chromium's
/// processes up with, a page closed while its downloads come in: it takes nothing the page offers and lets it ask for
/// nothing.</summary>
internal sealed class WebViewUnseenView : IWebViewBrowserView
{
    public void Show(WebViewBrowser browser, SharedSurfaceDescriptor descriptor)
    {
    }

    public void LetGo(WebViewBrowser browser)
    {
    }

    public void FrameWritten(WebViewBrowser browser)
    {
    }

    public void FrameRate(WebViewBrowser browser, int rate)
    {
    }

    public void SharedTextureRefused(WebViewBrowser browser)
    {
    }

    public void CursorChanged(WebViewBrowser browser, Cursor cursor)
    {
    }

    public bool StartDragging(WebViewBrowser browser, DataPackage data, DragDropEffects allowed) => false;

    public void AddressChanged(WebViewBrowser browser, string address)
    {
    }

    public void TitleChanged(WebViewBrowser browser, string title)
    {
    }

    public void LoadingChanged(WebViewBrowser browser, bool isLoading, bool canGoBack, bool canGoForward)
    {
    }

    public void ProgressChanged(WebViewBrowser browser, double progress)
    {
    }

    public void StatusChanged(WebViewBrowser browser, string status)
    {
    }

    public bool NavigationStarting(WebViewBrowser browser, string address, bool userGesture, bool isRedirect) =>
        address != WebViewBrowser.Blank;

    public void NavigationCompleted(WebViewBrowser browser, string address, WebViewLoadError error)
    {
    }

    public void MessageReceived(WebViewBrowser browser, string json)
    {
    }

    public IReadOnlyList<WebDocumentScript> ScriptsForNewDocuments(WebViewBrowser browser) => [];

    public void HostCalled(WebViewBrowser browser, WebHostCall call) => call.Refuse("No object is shown to this page.");

    public void ResourceRequested(WebViewBrowser browser, WebResourceRequestedEventArgs asked)
    {
    }

    public void HostContextGone(WebViewBrowser browser, string context)
    {
    }

    public void ZoomChanged(WebViewBrowser browser, double factor)
    {
    }

    public void LoadFailed(WebViewBrowser browser, WebViewLoadError error)
    {
    }

    public void CompositionBoundsChanged(WebViewBrowser browser, Rect bounds)
    {
    }

    public void IconChanged(WebViewBrowser browser, ImageSource icon)
    {
    }

    public void SecurityChanged(WebViewBrowser browser, bool isSecure)
    {
    }

    public void ShowPopup(WebViewBrowser browser, SharedSurfaceDescriptor descriptor)
    {
    }

    public void PopupPlaced(WebViewBrowser browser, Rect bounds)
    {
    }

    public void PopupClosed(WebViewBrowser browser)
    {
    }

    public bool ScriptDialogRequested(WebViewBrowser browser, WebViewScriptDialogRequest request) => false;

    public void ScriptDialogsReset(WebViewBrowser browser)
    {
    }

    public void TooltipChanged(WebViewBrowser browser, string text)
    {
    }

    public bool ContextMenuRequested(WebViewBrowser browser, WebViewContextMenu menu) => false;

    public bool NewWindowRequested(WebViewBrowser browser, WebViewNewPage page, WebNewWindowKind kind) => false;

    public void BrowserLost(WebViewBrowser browser)
    {
    }

    public bool FileDialogRequested(WebViewBrowser browser, WebViewFileDialogRequest request) => false;

    public bool PermissionRequested(WebViewBrowser browser, WebViewPermissionRequest request) => false;

    public bool DownloadRequested(WebViewBrowser browser, WebDownload download) => false;
}
