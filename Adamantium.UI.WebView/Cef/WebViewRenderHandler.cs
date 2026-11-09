using System.Runtime.InteropServices;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>A browser drawn off screen: CEF asks it the view's size and the screen's scale, and hands it each frame -
/// BGRA pixels, or its own texture where the browser was made to share one - with the rectangles that changed; the
/// page's and, while one is dropped, a list's of its own.</summary>
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
        handler->on_popup_show = &OnPopupShow;
        handler->on_popup_size = &OnPopupSize;
        handler->on_virtual_keyboard_requested = &OnVirtualKeyboardRequested;
        handler->on_ime_composition_range_changed = &OnImeCompositionRangeChanged;
        handler->start_dragging = &StartDragging;
        handler->update_drag_cursor = &UpdateDragCursor;
    }

    [UnmanagedCallersOnly]
    private static int StartDragging(CefRenderHandler* self, CefBrowser* cefBrowser, CefDragData* data,
        CefDragOperationsMask allowed, int x, int y)
    {
        var taken = false;
        try
        {
            var owner = From<WebViewRenderHandler>(self).browser;
            taken = owner.Is(cefBrowser) && owner.OnStartDragging(data, allowed);
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }

        CefRefCounting.ReleaseCef(data);
        CefRefCounting.ReleaseCef(cefBrowser);
        return taken ? 1 : 0;
    }

    [UnmanagedCallersOnly]
    private static void UpdateDragCursor(CefRenderHandler* self, CefBrowser* cefBrowser, CefDragOperationsMask operation)
    {
        try
        {
            var owner = From<WebViewRenderHandler>(self).browser;
            if (owner.Is(cefBrowser))
            {
                owner.OnDragCursor(operation);
            }
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }

        CefRefCounting.ReleaseCef(cefBrowser);
    }

    [UnmanagedCallersOnly]
    private static void OnVirtualKeyboardRequested(CefRenderHandler* self, CefBrowser* cefBrowser, CefTextInputMode mode)
    {
        try
        {
            var owner = From<WebViewRenderHandler>(self).browser;
            if (owner.Is(cefBrowser))
            {
                owner.OnTextInputMode(mode);
            }
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }

        CefRefCounting.ReleaseCef(cefBrowser);
    }

    [UnmanagedCallersOnly]
    private static void OnImeCompositionRangeChanged(CefRenderHandler* self, CefBrowser* cefBrowser, CefRange* selected,
        ulong boundsCount, CefRect* characterBounds)
    {
        try
        {
            var owner = From<WebViewRenderHandler>(self).browser;
            if (boundsCount > 0 && owner.Is(cefBrowser))
            {
                owner.OnCompositionBounds(characterBounds[boundsCount - 1]);
            }
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }

        CefRefCounting.ReleaseCef(cefBrowser);
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

    // A list the page drops - a <select>'s - is drawn by Chromium apart from the page: shown or hidden, placed, then
    // painted as a frame of its own.
    [UnmanagedCallersOnly]
    private static void OnPopupShow(CefRenderHandler* self, CefBrowser* cefBrowser, int show)
    {
        try
        {
            var owner = From<WebViewRenderHandler>(self).browser;
            if (owner.Is(cefBrowser))
            {
                owner.OnPopupShow(show != 0);
            }
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }

        CefRefCounting.ReleaseCef(cefBrowser);
    }

    [UnmanagedCallersOnly]
    private static void OnPopupSize(CefRenderHandler* self, CefBrowser* cefBrowser, CefRect* rect)
    {
        try
        {
            var owner = From<WebViewRenderHandler>(self).browser;
            if (owner.Is(cefBrowser))
            {
                owner.OnPopupSize(*rect);
            }
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }

        CefRefCounting.ReleaseCef(cefBrowser);
    }

    [UnmanagedCallersOnly]
    private static void OnPaint(CefRenderHandler* self, CefBrowser* cefBrowser, CefPaintElementType type, ulong dirtyCount,
        CefRect* dirtyRects, void* buffer, int width, int height)
    {
        try
        {
            var owner = From<WebViewRenderHandler>(self).browser;
            if (owner.Is(cefBrowser))
            {
                owner.OnPaint(type, new ReadOnlySpan<CefRect>(dirtyRects, (int)dirtyCount), (byte*)buffer, width,
                    height);
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
            if (OperatingSystem.IsWindows() && owner.Is(cefBrowser))
            {
                var paint = (Interop.Windows.CefAcceleratedPaintInfo*)info;
                var visible = paint->extra.visible_rect;
                owner.OnAcceleratedPaint(type, new ReadOnlySpan<CefRect>(dirtyRects, (int)dirtyCount),
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
