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
    private readonly WebViewLoadHandler loadHandler;
    private readonly WebViewRequestHandler requestHandler;
    private readonly WebViewContextMenuHandler contextMenuHandler;
    private readonly WebViewJsDialogHandler jsDialogHandler;

    public WebViewClient(WebViewRenderHandler renderHandler, WebViewLifeSpanHandler lifeSpanHandler,
        WebViewDisplayHandler displayHandler, WebViewLoadHandler loadHandler, WebViewRequestHandler requestHandler,
        WebViewContextMenuHandler contextMenuHandler, WebViewJsDialogHandler jsDialogHandler)
        : base(sizeof(CefClient))
    {
        this.jsDialogHandler = jsDialogHandler;
        jsDialogHandler.AddReference();
        this.renderHandler = renderHandler;
        this.lifeSpanHandler = lifeSpanHandler;
        this.displayHandler = displayHandler;
        this.loadHandler = loadHandler;
        this.requestHandler = requestHandler;
        this.contextMenuHandler = contextMenuHandler;
        renderHandler.AddReference();
        lifeSpanHandler.AddReference();
        displayHandler.AddReference();
        loadHandler.AddReference();
        requestHandler.AddReference();
        contextMenuHandler.AddReference();
        var client = (CefClient*)Native;
        client->get_render_handler = &GetRenderHandler;
        client->get_life_span_handler = &GetLifeSpanHandler;
        client->get_display_handler = &GetDisplayHandler;
        client->get_load_handler = &GetLoadHandler;
        client->get_request_handler = &GetRequestHandler;
        client->get_context_menu_handler = &GetContextMenuHandler;
        client->get_jsdialog_handler = &GetJsDialogHandler;
    }

    [UnmanagedCallersOnly]
    private static CefJsdialogHandler* GetJsDialogHandler(CefClient* self)
    {
        return (CefJsdialogHandler*)From<WebViewClient>(self).jsDialogHandler.Share();
    }

    [UnmanagedCallersOnly]
    private static CefContextMenuHandler* GetContextMenuHandler(CefClient* self)
    {
        return (CefContextMenuHandler*)From<WebViewClient>(self).contextMenuHandler.Share();
    }

    /// <summary>The struct with a reference taken for CEF.</summary>
    public CefClient* Shared => (CefClient*)Share();

    protected override void OnFreed()
    {
        renderHandler.Release();
        lifeSpanHandler.Release();
        displayHandler.Release();
        loadHandler.Release();
        requestHandler.Release();
        contextMenuHandler.Release();
        jsDialogHandler.Release();
    }

    [UnmanagedCallersOnly]
    private static CefRequestHandler* GetRequestHandler(CefClient* self)
    {
        return (CefRequestHandler*)From<WebViewClient>(self).requestHandler.Share();
    }

    [UnmanagedCallersOnly]
    private static CefDisplayHandler* GetDisplayHandler(CefClient* self)
    {
        return (CefDisplayHandler*)From<WebViewClient>(self).displayHandler.Share();
    }

    [UnmanagedCallersOnly]
    private static CefLoadHandler* GetLoadHandler(CefClient* self)
    {
        return (CefLoadHandler*)From<WebViewClient>(self).loadHandler.Share();
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
