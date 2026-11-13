using System.Runtime.InteropServices;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>A browser's search of its page: how many matches there are and which one is current, as Chromium counts
/// them.</summary>
internal sealed unsafe class WebViewFindHandler : CefObject
{
    private readonly WebViewBrowser browser;

    public WebViewFindHandler(WebViewBrowser browser) : base(sizeof(CefFindHandler))
    {
        this.browser = browser;
        var handler = (CefFindHandler*)Native;
        handler->on_find_result = &OnFindResult;
    }

    [UnmanagedCallersOnly]
    private static void OnFindResult(CefFindHandler* self, CefBrowser* cefBrowser, int identifier, int count,
        CefRect* selection, int activeMatchOrdinal, int finalUpdate)
    {
        try
        {
            var owner = From<WebViewFindHandler>(self).browser;
            if (owner.Is(cefBrowser))
            {
                owner.OnFindResult(count, activeMatchOrdinal, finalUpdate != 0);
            }
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }

        CefRefCounting.ReleaseCef(cefBrowser);
    }
}
