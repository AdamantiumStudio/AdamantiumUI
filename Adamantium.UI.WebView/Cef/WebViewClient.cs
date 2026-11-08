using System.Runtime.InteropServices;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>A browser's client: hands CEF the handlers of one <see cref="WebViewBrowser"/>, which it holds as long as
/// CEF holds it - CEF asks for a handler each time it calls one, up to the browser's last callback.</summary>
internal sealed unsafe class WebViewClient : CefObject
{
    private readonly WebViewRenderHandler renderHandler;
    private readonly WebViewLifeSpanHandler lifeSpanHandler;
    private readonly WebViewDisplayHandler displayHandler;

    public WebViewClient(WebViewRenderHandler renderHandler, WebViewLifeSpanHandler lifeSpanHandler,
        WebViewDisplayHandler displayHandler)
        : base(sizeof(CefClient))
    {
        this.renderHandler = renderHandler;
        this.lifeSpanHandler = lifeSpanHandler;
        this.displayHandler = displayHandler;
        renderHandler.AddReference();
        lifeSpanHandler.AddReference();
        displayHandler.AddReference();
        var client = (CefClient*)Native;
        client->get_render_handler = &GetRenderHandler;
        client->get_life_span_handler = &GetLifeSpanHandler;
        client->get_display_handler = &GetDisplayHandler;
    }

    /// <summary>The struct with a reference taken for CEF.</summary>
    public CefClient* Shared => (CefClient*)Share();

    protected override void OnFreed()
    {
        renderHandler.Release();
        lifeSpanHandler.Release();
        displayHandler.Release();
    }

    [UnmanagedCallersOnly]
    private static CefDisplayHandler* GetDisplayHandler(CefClient* self)
    {
        return (CefDisplayHandler*)From<WebViewClient>(self).displayHandler.Share();
    }

    [UnmanagedCallersOnly]
    private static CefRenderHandler* GetRenderHandler(CefClient* self)
    {
        return (CefRenderHandler*)From<WebViewClient>(self).renderHandler.Share();
    }

    [UnmanagedCallersOnly]
    private static CefLifeSpanHandler* GetLifeSpanHandler(CefClient* self)
    {
        return (CefLifeSpanHandler*)From<WebViewClient>(self).lifeSpanHandler.Share();
    }
}
