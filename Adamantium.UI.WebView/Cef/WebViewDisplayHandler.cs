using System.Runtime.InteropServices;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>What a browser shows outside its frame: the address, title and icons of its page, how far it has loaded,
/// the address of the link under the pointer, and the cursor its page asks for.</summary>
internal sealed unsafe class WebViewDisplayHandler : CefObject
{
    private readonly WebViewBrowser browser;

    public WebViewDisplayHandler(WebViewBrowser browser) : base(sizeof(CefDisplayHandler))
    {
        this.browser = browser;
        var handler = (CefDisplayHandler*)Native;
        handler->on_cursor_change = &OnCursorChange;
        handler->on_address_change = &OnAddressChange;
        handler->on_title_change = &OnTitleChange;
        handler->on_status_message = &OnStatusMessage;
        handler->on_loading_progress_change = &OnLoadingProgressChange;
        handler->on_favicon_urlchange = &OnFaviconUrlChange;
    }

    [UnmanagedCallersOnly]
    private static void OnFaviconUrlChange(CefDisplayHandler* self, CefBrowser* cefBrowser, CefStringList addresses)
    {
        try
        {
            var owner = From<WebViewDisplayHandler>(self).browser;
            if (owner.Is(cefBrowser))
            {
                owner.OnIconAddresses(CefStrings.ReadList(addresses));
            }
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }

        CefRefCounting.ReleaseCef(cefBrowser);
    }

    [UnmanagedCallersOnly]
    private static void OnAddressChange(CefDisplayHandler* self, CefBrowser* cefBrowser, CefFrame* frame,
        CefStringUtf16* address)
    {
        try
        {
            var owner = From<WebViewDisplayHandler>(self).browser;
            if (frame->is_main(frame) != 0 && owner.Is(cefBrowser))
            {
                owner.OnAddressChange(CefStrings.Read(address));
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
    private static void OnTitleChange(CefDisplayHandler* self, CefBrowser* cefBrowser, CefStringUtf16* title)
    {
        try
        {
            var owner = From<WebViewDisplayHandler>(self).browser;
            if (owner.Is(cefBrowser))
            {
                owner.OnTitleChange(CefStrings.Read(title));
            }
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }

        CefRefCounting.ReleaseCef(cefBrowser);
    }

    [UnmanagedCallersOnly]
    private static void OnStatusMessage(CefDisplayHandler* self, CefBrowser* cefBrowser, CefStringUtf16* message)
    {
        try
        {
            var owner = From<WebViewDisplayHandler>(self).browser;
            if (owner.Is(cefBrowser))
            {
                owner.OnStatusMessage(CefStrings.Read(message));
            }
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }

        CefRefCounting.ReleaseCef(cefBrowser);
    }

    [UnmanagedCallersOnly]
    private static void OnLoadingProgressChange(CefDisplayHandler* self, CefBrowser* cefBrowser, double progress)
    {
        try
        {
            var owner = From<WebViewDisplayHandler>(self).browser;
            if (owner.Is(cefBrowser))
            {
                owner.OnLoadingProgress(progress);
            }
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }

        CefRefCounting.ReleaseCef(cefBrowser);
    }

    [UnmanagedCallersOnly]
    private static int OnCursorChange(CefDisplayHandler* self, CefBrowser* cefBrowser, void* cursor, CefCursorType type,
        CefCursorInfo* customCursor)
    {
        try
        {
            var owner = From<WebViewDisplayHandler>(self).browser;
            if (owner.Is(cefBrowser))
            {
                owner.OnCursorChange(type);
            }
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }

        CefRefCounting.ReleaseCef(cefBrowser);
        return 1;
    }
}
