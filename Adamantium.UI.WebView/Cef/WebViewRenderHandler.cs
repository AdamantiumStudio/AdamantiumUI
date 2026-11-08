using System.Runtime.InteropServices;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>A browser drawn off screen: CEF asks it the view's size and the screen's scale, and hands it each frame -
/// BGRA pixels, or its own texture where the browser was made to share one - with the rectangles that changed.</summary>
internal sealed unsafe class WebViewRenderHandler : CefObject
{
    private readonly WebViewBrowser browser;

    public WebViewRenderHandler(WebViewBrowser browser) : base(sizeof(CefRenderHandler))
    {
        this.browser = browser;
        var handler = (CefRenderHandler*)Native;
        handler->get_view_rect = &GetViewRect;
        handler->get_screen_info = &GetScreenInfo;
        handler->on_paint = &OnPaint;
        handler->on_accelerated_paint = &OnAcceleratedPaint;
    }

    [UnmanagedCallersOnly]
    private static void GetViewRect(CefRenderHandler* self, CefBrowser* cefBrowser, CefRect* rect)
    {
        var (width, height) = From<WebViewRenderHandler>(self).browser.ViewSize;        *rect = new CefRect { x = 0, y = 0, width = width, height = height };
        CefRefCounting.ReleaseCef(cefBrowser);
    }

    [UnmanagedCallersOnly]
    private static int GetScreenInfo(CefRenderHandler* self, CefBrowser* cefBrowser, CefScreenInfo* screenInfo)
    {
        screenInfo->device_scale_factor = From<WebViewRenderHandler>(self).browser.Scale;
        CefRefCounting.ReleaseCef(cefBrowser);
        return 1;
    }

    [UnmanagedCallersOnly]
    private static void OnPaint(CefRenderHandler* self, CefBrowser* cefBrowser, CefPaintElementType type, ulong dirtyCount,
        CefRect* dirtyRects, void* buffer, int width, int height)
    {
        try
        {
            var owner = From<WebViewRenderHandler>(self).browser;            if (type == CefPaintElementType.View && owner.Is(cefBrowser))
            {
                owner.OnPaint(new ReadOnlySpan<CefRect>(dirtyRects, (int)dirtyCount), (byte*)buffer, width, height);
            }
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }

        CefRefCounting.ReleaseCef(cefBrowser);
    }

    [UnmanagedCallersOnly]
    private static void OnAcceleratedPaint(CefRenderHandler* self, CefBrowser* cefBrowser, CefPaintElementType type,
        ulong dirtyCount, CefRect* dirtyRects, void* info)
    {
        try
        {
            var owner = From<WebViewRenderHandler>(self).browser;
            if (OperatingSystem.IsWindows() && type == CefPaintElementType.View && owner.Is(cefBrowser))
            {
                var paint = (Interop.Windows.CefAcceleratedPaintInfo*)info;
                var visible = paint->extra.visible_rect;
                owner.OnAcceleratedPaint(new ReadOnlySpan<CefRect>(dirtyRects, (int)dirtyCount),
                    (IntPtr)paint->shared_texture_handle.pointer, paint->format, paint->extra.coded_size.width,
                    paint->extra.coded_size.height, visible);
            }
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }

        CefRefCounting.ReleaseCef(cefBrowser);
    }
}
