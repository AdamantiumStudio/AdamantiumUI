using System.Runtime.InteropServices;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>A browser's loading: whether it is loading and can go back or forward, and a page that loaded or failed to
/// load in its main frame.</summary>
internal sealed unsafe class WebViewLoadHandler : CefObject
{
    private readonly WebViewBrowser browser;

    public WebViewLoadHandler(WebViewBrowser browser) : base(sizeof(CefLoadHandler))
    {
        this.browser = browser;
        var handler = (CefLoadHandler*)Native;
        handler->on_loading_state_change = &OnLoadingStateChange;
        handler->on_load_error = &OnLoadError;
        handler->on_load_end = &OnLoadEnd;
        handler->on_load_start = &OnLoadStart;
    }

    [UnmanagedCallersOnly]
    private static void OnLoadStart(CefLoadHandler* self, CefBrowser* cefBrowser, CefFrame* frame,
        CefTransitionType transition)
    {
        try
        {
            var owner = From<WebViewLoadHandler>(self).browser;
            if (frame->is_main(frame) != 0 && owner.Is(cefBrowser))
            {
                owner.OnLoadStart();
            }
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }

        CefRefCounting.ReleaseCef(frame);
        CefRefCounting.ReleaseCef(cefBrowser);
    }

    [UnmanagedCallersOnly]
    private static void OnLoadEnd(CefLoadHandler* self, CefBrowser* cefBrowser, CefFrame* frame, int status)
    {
        try
        {
            var owner = From<WebViewLoadHandler>(self).browser;
            if (frame->is_main(frame) != 0 && owner.Is(cefBrowser))
            {
                owner.OnLoadEnd(CefStrings.ReadAndFree(frame->get_url(frame)));
            }
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }

        CefRefCounting.ReleaseCef(frame);
        CefRefCounting.ReleaseCef(cefBrowser);
    }

    [UnmanagedCallersOnly]
    private static void OnLoadingStateChange(CefLoadHandler* self, CefBrowser* cefBrowser, int isLoading, int canGoBack,
        int canGoForward)
    {
        try
        {
            var owner = From<WebViewLoadHandler>(self).browser;
            if (owner.Is(cefBrowser))
            {
                owner.OnLoadingState(isLoading != 0, canGoBack != 0, canGoForward != 0);
            }
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }

        CefRefCounting.ReleaseCef(cefBrowser);
    }

    [UnmanagedCallersOnly]
    private static void OnLoadError(CefLoadHandler* self, CefBrowser* cefBrowser, CefFrame* frame, CefErrorcode code,
        CefStringUtf16* text, CefStringUtf16* address)
    {
        try
        {
            var owner = From<WebViewLoadHandler>(self).browser;
            if (code != CefErrorcode.Aborted && frame->is_main(frame) != 0 && owner.Is(cefBrowser))
            {
                owner.OnLoadError(new WebViewLoadError((int)code, CefStrings.Read(text), CefStrings.Read(address)));
            }
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }

        CefRefCounting.ReleaseCef(frame);
        CefRefCounting.ReleaseCef(cefBrowser);
    }
}
