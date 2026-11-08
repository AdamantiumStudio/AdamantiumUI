using System.Runtime.InteropServices;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>A browser's requests: the page about to go somewhere in its main frame, which the view may stop.</summary>
internal sealed unsafe class WebViewRequestHandler : CefObject
{
    private readonly WebViewBrowser browser;

    public WebViewRequestHandler(WebViewBrowser browser) : base(sizeof(CefRequestHandler))
    {
        this.browser = browser;
        ((CefRequestHandler*)Native)->on_before_browse = &OnBeforeBrowse;
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
