using System.Runtime.InteropServices;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>A browser's requests: the page about to go somewhere in its main frame, which the view may stop, and a link
/// it opens elsewhere, which the view may open apart.</summary>
internal sealed unsafe class WebViewRequestHandler : CefObject
{
    private readonly WebViewBrowser browser;

    // Held here, as long as CEF holds this handler: CEF asks for it on its IO thread up to the browser's last request.
    private readonly WebViewResourceRequestHandler resourceRequests;

    public WebViewRequestHandler(WebViewBrowser browser) : base(sizeof(CefRequestHandler))
    {
        this.browser = browser;
        resourceRequests = new WebViewResourceRequestHandler(browser);
        ((CefRequestHandler*)Native)->on_before_browse = &OnBeforeBrowse;
        ((CefRequestHandler*)Native)->on_open_urlfrom_tab = &OnOpenUrlFromTab;
        ((CefRequestHandler*)Native)->get_resource_request_handler = &GetResourceRequestHandler;
    }

    /// <summary>Refuses the page's requests still waiting for the view: the browser closes.</summary>
    public void RefuseWaiting() => resourceRequests.RefuseWaiting();

    protected override void OnFreed() => resourceRequests.Release();

    // Asked on CEF's IO thread for each request: the view is asked of it only while it listens.
    [UnmanagedCallersOnly]
    private static CefResourceRequestHandler* GetResourceRequestHandler(CefRequestHandler* self, CefBrowser* cefBrowser,
        CefFrame* frame, CefRequest* request, int isNavigation, int isDownload, CefStringUtf16* requestInitiator,
        int* disableDefaultHandling)
    {
        CefResourceRequestHandler* handler = null;
        try
        {
            var owner = From<WebViewRequestHandler>(self);
            if (owner.browser.InterceptsResources)
            {
                handler = owner.resourceRequests.Shared;
            }
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }

        CefRefCounting.ReleaseCef(cefBrowser);
        CefRefCounting.ReleaseCef(frame);
        CefRefCounting.ReleaseCef(request);
        return handler;
    }

    [UnmanagedCallersOnly]
    private static int OnOpenUrlFromTab(CefRequestHandler* self, CefBrowser* cefBrowser, CefFrame* frame,
        CefStringUtf16* targetUrl, CefWindowOpenDisposition disposition, int userGesture)
    {
        var opened = false;
        try
        {
            var owner = From<WebViewRequestHandler>(self).browser;
            if (owner.Is(cefBrowser))
            {
                opened = owner.OnOpenElsewhere(CefStrings.Read(targetUrl), disposition, userGesture != 0);
            }
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }

        CefRefCounting.ReleaseCef(frame);
        CefRefCounting.ReleaseCef(cefBrowser);
        return opened ? 1 : 0;
    }

    [UnmanagedCallersOnly]
    private static int OnBeforeBrowse(CefRequestHandler* self, CefBrowser* cefBrowser, CefFrame* frame,
        CefRequest* request, int userGesture, int isRedirect)
    {
        var cancel = false;
        try
        {
            var owner = From<WebViewRequestHandler>(self).browser;
            if (frame->is_main(frame) != 0 && owner.Is(cefBrowser))
            {
                cancel = owner.OnBeforeBrowse(CefStrings.ReadAndFree(request->get_url(request)), userGesture != 0,
                    isRedirect != 0);
            }
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }

        CefRefCounting.ReleaseCef(request);
        CefRefCounting.ReleaseCef(frame);
        CefRefCounting.ReleaseCef(cefBrowser);
        return cancel ? 1 : 0;
    }
}
