using System.Runtime.InteropServices;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>A browser's downloads: each is asked of the view, which says where to save it - never a dialog of
/// Chromium's own - and followed as it comes in. Called on the UI loop, where CEF's browser thread runs.</summary>
internal sealed unsafe class WebViewDownloadHandler : CefObject
{
    private readonly WebViewBrowser browser;

    public WebViewDownloadHandler(WebViewBrowser browser) : base(sizeof(CefDownloadHandler))
    {
        this.browser = browser;
        var handler = (CefDownloadHandler*)Native;
        handler->can_download = &CanDownload;
        handler->on_before_download = &OnBeforeDownload;
        handler->on_download_updated = &OnDownloadUpdated;
    }

    // Any download may start: it is asked of the view before anything is saved.
    [UnmanagedCallersOnly]
    private static int CanDownload(CefDownloadHandler* self, CefBrowser* cefBrowser, CefStringUtf16* url,
        CefStringUtf16* requestMethod)
    {
        CefRefCounting.ReleaseCef(cefBrowser);
        return 1;
    }

    // Asked of the view: taken, Chromium waits for the answer; not taken, it cancels the download.
    [UnmanagedCallersOnly]
    private static int OnBeforeDownload(CefDownloadHandler* self, CefBrowser* cefBrowser, CefDownloadItem* item,
        CefStringUtf16* suggestedName, CefBeforeDownloadCallback* callback)
    {
        var taken = false;
        try
        {
            var owner = From<WebViewDownloadHandler>(self).browser;
            var download = owner.Download(item->get_id(item));
            download.Origin = owner;
            download.Read(item);
            download.Suggest(CefStrings.Read(suggestedName));
            download.Ask(callback);
            taken = owner.Is(cefBrowser) && owner.OnDownloadRequested(download);
            if (!taken)
            {
                download.Refuse();
            }
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }

        CefRefCounting.ReleaseCef(callback);
        CefRefCounting.ReleaseCef(item);
        CefRefCounting.ReleaseCef(cefBrowser);
        return taken ? 1 : 0;
    }

    [UnmanagedCallersOnly]
    private static void OnDownloadUpdated(CefDownloadHandler* self, CefBrowser* cefBrowser, CefDownloadItem* item,
        CefDownloadItemCallback* callback)
    {
        try
        {
            var owner = From<WebViewDownloadHandler>(self).browser;
            var download = owner.Download(item->get_id(item));
            download.Origin = owner;
            download.Read(item);
            download.TakeControl(callback);
            owner.DownloadHeard(download);
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }

        CefRefCounting.ReleaseCef(callback);
        CefRefCounting.ReleaseCef(item);
        CefRefCounting.ReleaseCef(cefBrowser);
    }
}
