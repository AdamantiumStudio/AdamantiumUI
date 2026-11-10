using System.Runtime.InteropServices;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>A browser's life: CEF hands over the browser once it is made, asks before a page opens a window of its own -
/// a browser of another client, drawn off screen for the view that shows it, or refused - and says when the browser is
/// about to go.</summary>
internal sealed unsafe class WebViewLifeSpanHandler : CefObject
{
    private readonly WebViewBrowser browser;

    public WebViewLifeSpanHandler(WebViewBrowser browser) : base(sizeof(CefLifeSpanHandler))
    {
        this.browser = browser;
        var handler = (CefLifeSpanHandler*)Native;
        handler->on_before_popup = &OnBeforePopup;
        handler->on_before_popup_aborted = &OnBeforePopupAborted;
        handler->on_after_created = &OnAfterCreated;
        handler->on_before_close = &OnBeforeClose;
    }

    [UnmanagedCallersOnly]
    private static int OnBeforePopup(CefLifeSpanHandler* self, CefBrowser* cefBrowser, CefFrame* frame, int popupId,
        CefStringUtf16* targetUrl, CefStringUtf16* targetFrameName, CefWindowOpenDisposition disposition, int userGesture,
        CefPopupFeatures* features, void* windowInfo, CefClient** client, CefBrowserSettings* settings,
        CefDictionaryValue** extraInfo, int* noJavascriptAccess)
    {
        // What comes in through the out parameters carries a reference of ours, as the other arguments do: CEF keeps
        // its own for what is left as it was, and takes the one handed back with what is put in its place.
        var lentClient = client == null ? null : *client;
        var lentExtraInfo = extraInfo == null ? null : *extraInfo;
        var allowed = false;
        try
        {
            allowed = From<WebViewLifeSpanHandler>(self).browser.OnBeforePopup(popupId, CefStrings.Read(targetUrl),
                disposition, userGesture != 0, windowInfo, client, settings);
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }

        CefRefCounting.ReleaseCef(lentClient);
        CefRefCounting.ReleaseCef(lentExtraInfo);
        CefRefCounting.ReleaseCef(frame);
        CefRefCounting.ReleaseCef(cefBrowser);
        return allowed ? 0 : 1;
    }

    [UnmanagedCallersOnly]
    private static void OnBeforePopupAborted(CefLifeSpanHandler* self, CefBrowser* cefBrowser, int popupId)
    {
        try
        {
            From<WebViewLifeSpanHandler>(self).browser.OnPopupAborted(popupId);
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }

        CefRefCounting.ReleaseCef(cefBrowser);
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
