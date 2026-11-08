using System.Runtime.InteropServices;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>A browser's life: CEF hands over the browser once it is made, asks before a page opens a window of its own -
/// refused, as it would be a browser of this client drawn into this view's surfaces - and says when the browser is about
/// to go.</summary>
internal sealed unsafe class WebViewLifeSpanHandler : CefObject
{
    private readonly WebViewBrowser browser;

    public WebViewLifeSpanHandler(WebViewBrowser browser) : base(sizeof(CefLifeSpanHandler))
    {
        this.browser = browser;
        var handler = (CefLifeSpanHandler*)Native;
        handler->on_before_popup = &OnBeforePopup;
        handler->on_after_created = &OnAfterCreated;
        handler->on_before_close = &OnBeforeClose;
    }

    [UnmanagedCallersOnly]
    private static int OnBeforePopup(CefLifeSpanHandler* self, CefBrowser* cefBrowser, CefFrame* frame, int popupId,
        CefStringUtf16* targetUrl, CefStringUtf16* targetFrameName, CefWindowOpenDisposition disposition, int userGesture,
        CefPopupFeatures* features, void* windowInfo, CefClient** client, CefBrowserSettings* settings,
        CefDictionaryValue** extraInfo, int* noJavascriptAccess)
    {
        try
        {
            From<WebViewLifeSpanHandler>(self).browser.OnPopup(CefStrings.Read(targetUrl), userGesture != 0);
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }

        CefRefCounting.ReleaseCef(frame);
        CefRefCounting.ReleaseCef(cefBrowser);
        return 1;
    }

    [UnmanagedCallersOnly]
    private static void OnAfterCreated(CefLifeSpanHandler* self, CefBrowser* cefBrowser)
    {
        try
        {
            From<WebViewLifeSpanHandler>(self).browser.OnCreated(cefBrowser);
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }
    }

    [UnmanagedCallersOnly]
    private static void OnBeforeClose(CefLifeSpanHandler* self, CefBrowser* cefBrowser)
    {
        try
        {
            From<WebViewLifeSpanHandler>(self).browser.OnClosing(cefBrowser);
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }

        CefRefCounting.ReleaseCef(cefBrowser);
    }
}
