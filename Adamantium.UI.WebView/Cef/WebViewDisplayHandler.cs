using System.Runtime.InteropServices;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>What a browser shows outside its frame: the cursor its page asks for over it.</summary>
internal sealed unsafe class WebViewDisplayHandler : CefObject
{
    private readonly WebViewBrowser browser;

    public WebViewDisplayHandler(WebViewBrowser browser) : base(sizeof(CefDisplayHandler))
    {
        this.browser = browser;
        ((CefDisplayHandler*)Native)->on_cursor_change = &OnCursorChange;
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
