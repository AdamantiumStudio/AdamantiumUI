using System.Runtime.InteropServices;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>Hears what the DevTools protocol of a browser answers and tells: each message, as the UTF-8 JSON it is.
/// </summary>
internal sealed unsafe class WebViewDevToolsObserver : CefObject
{
    private readonly WebViewDevTools devTools;

    public WebViewDevToolsObserver(WebViewDevTools devTools) : base(sizeof(CefDevToolsMessageObserver))
    {
        this.devTools = devTools;
        ((CefDevToolsMessageObserver*)Native)->on_dev_tools_message = &OnDevToolsMessage;
    }

    /// <summary>The struct with a reference taken for CEF.</summary>
    public CefDevToolsMessageObserver* Shared => (CefDevToolsMessageObserver*)Share();

    [UnmanagedCallersOnly]
    private static int OnDevToolsMessage(CefDevToolsMessageObserver* self, CefBrowser* cefBrowser, void* message,
        ulong size)
    {
        try
        {
            From<WebViewDevToolsObserver>(self).devTools.OnMessage(cefBrowser,
                new ReadOnlySpan<byte>(message, (int)size));
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }

        CefRefCounting.ReleaseCef(cefBrowser);
        return 1;
    }
}
