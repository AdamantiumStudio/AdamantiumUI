using Adamantium.Graphics.Core;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Media.Imaging;

namespace Adamantium.UI.WebView.Cef;

/// <summary>
/// The view of a page Chromium made for another to open, until the view that shows it takes it up: what the page says
/// meanwhile - where it is, its title, whether it loads, its icon - is kept and told to that view then. Its frames go
/// nowhere, and what it asks of a user who cannot see it - a dialog, a menu, another page - is refused.
/// </summary>
internal sealed class WebViewWaitingView : IWebViewBrowserView
{
    private readonly IReadOnlyList<WebDocumentScript> openersScripts;
    private readonly Action<WebResourceRequestedEventArgs> openersRequests;
    private readonly Func<WebDownload, bool> openersDownloads;
    private string address;
    private string title;
    private bool isLoading;
    private bool canGoBack;
    private bool canGoForward;
    private double progress;
    private double zoom = 1;
    private bool zoomTold;
    private WebViewLoadError error;
    private ImageSource icon;
    private bool? isSecure;

    /// <summary>A view for a page the page with <paramref name="openersScripts"/> opens: its documents run those, its
    /// requests are asked of <paramref name="openersRequests"/> and its downloads of <paramref name="openersDownloads"/>,
    /// until a view takes it up - it is the application's page as much as its opener. A link to a file opened as a new
    /// page downloads it this way.</summary>
    public WebViewWaitingView(IReadOnlyList<WebDocumentScript> openersScripts,
        Action<WebResourceRequestedEventArgs> openersRequests, Func<WebDownload, bool> openersDownloads)
    {
        this.openersScripts = openersScripts;
        this.openersRequests = openersRequests;
        this.openersDownloads = openersDownloads;
    }

    /// <summary>Whether the browser can no longer be shown as it is: CEF gave it up, or its frames cannot come as they
    /// were to - the view that shows the page makes one of its own.</summary>
    public bool IsLost { get; private set; }

    /// <summary>Where the page went while it waited; null before it said.</summary>
    public string Address => address;

    /// <summary>Lets go of what the page gave and nobody will show: its icon.</summary>
    public void Discard()
    {
        icon?.Dispose();
        icon = null;
    }

    /// <summary>Tells <paramref name="view"/> what the page said while it waited.</summary>
    public void Replay(IWebViewBrowserView view, WebViewBrowser browser)
    {
        if (address != null)
        {
            view.AddressChanged(browser, address);
        }

        if (title != null)
        {
            view.TitleChanged(browser, title);
        }

        view.LoadingChanged(browser, isLoading, canGoBack, canGoForward);
        view.ProgressChanged(browser, progress);
        if (zoomTold)
        {
            view.ZoomChanged(browser, zoom);
        }

        if (error != null)
        {
            view.LoadFailed(browser, error);
        }

        // Handed on: the view owns the icon from now on.
        view.IconChanged(browser, icon);
        icon = null;
        if (isSecure is { } secure)
        {
            view.SecurityChanged(browser, secure);
        }
    }

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

    public void SharedTextureRefused(WebViewBrowser browser) => IsLost = true;

    public void CursorChanged(WebViewBrowser browser, Cursor cursor)
    {
    }

    public bool StartDragging(WebViewBrowser browser, DataPackage data, DragDropEffects allowed) => false;

    public void AddressChanged(WebViewBrowser browser, string address) => this.address = address;

    public void TitleChanged(WebViewBrowser browser, string title) => this.title = title;

    public void LoadingChanged(WebViewBrowser browser, bool isLoading, bool canGoBack, bool canGoForward)
    {
        this.isLoading = isLoading;
        this.canGoBack = canGoBack;
        this.canGoForward = canGoForward;
    }

    public void ProgressChanged(WebViewBrowser browser, double progress) => this.progress = progress;

    public void StatusChanged(WebViewBrowser browser, string status)
    {
    }

    public bool NavigationStarting(WebViewBrowser browser, string address, bool userGesture, bool isRedirect) => false;

    public void NavigationCompleted(WebViewBrowser browser, string address, WebViewLoadError error)
    {
    }

    public void MessageReceived(WebViewBrowser browser, string json)
    {
    }

    public IReadOnlyList<WebDocumentScript> ScriptsForNewDocuments(WebViewBrowser browser) => openersScripts;

    public void HostCalled(WebViewBrowser browser, WebHostCall call) => call.Refuse("The page is not shown yet.");

    public void ResourceRequested(WebViewBrowser browser, WebResourceRequestedEventArgs asked) => openersRequests(asked);

    public void HostContextGone(WebViewBrowser browser, string context)
    {
    }

    public void ZoomChanged(WebViewBrowser browser, double factor)
    {
        zoom = factor;
        zoomTold = true;
    }

    public void LoadFailed(WebViewBrowser browser, WebViewLoadError error) => this.error = error;

    public void CompositionBoundsChanged(WebViewBrowser browser, Rect bounds)
    {
    }

    public void IconChanged(WebViewBrowser browser, ImageSource icon)
    {
        if (!ReferenceEquals(this.icon, icon))
        {
            this.icon?.Dispose();
        }

        this.icon = icon;
    }

    public void SecurityChanged(WebViewBrowser browser, bool isSecure) => this.isSecure = isSecure;

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

    public void BrowserLost(WebViewBrowser browser) => IsLost = true;

    public bool FileDialogRequested(WebViewBrowser browser, WebViewFileDialogRequest request) => false;

    public bool PermissionRequested(WebViewBrowser browser, WebViewPermissionRequest request) => false;

    public bool DownloadRequested(WebViewBrowser browser, WebDownload download) => openersDownloads(download);

    // Nobody searches a page before a view adopts it.
    public void FindResult(WebViewBrowser browser, int count, int activeMatch, bool final)
    {
    }
}
