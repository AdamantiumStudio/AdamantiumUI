using System.Runtime.InteropServices;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>The application CEF is initialized with: it hands CEF the browser process's handler.</summary>
internal sealed unsafe class WebViewApp : CefObject
{
    private readonly WebViewBrowserProcessHandler browserProcess;

    public WebViewApp(WebViewBrowserProcessHandler browserProcess) : base(sizeof(CefApp))
    {
        this.browserProcess = browserProcess;
        ((CefApp*)Native)->get_browser_process_handler = &GetBrowserProcessHandler;
    }

    /// <summary>The struct with a reference taken for CEF.</summary>
    public CefApp* Shared => (CefApp*)Share();

    [UnmanagedCallersOnly]
    private static CefBrowserProcessHandler* GetBrowserProcessHandler(CefApp* self)
    {
        return (CefBrowserProcessHandler*)From<WebViewApp>(self).browserProcess.Share();
    }
}
