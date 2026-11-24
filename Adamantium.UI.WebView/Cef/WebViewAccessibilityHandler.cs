using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>What Chromium says of the page's accessibility tree, once it was asked to keep one: its nodes as they change,
/// and where they moved - as UTF-8 JSON, for the browser's copy of the tree. The JSON goes from CEF's own string to UTF-8
/// in a pooled buffer, with no string of its own: a heavy page's first tree is megabytes.</summary>
internal sealed unsafe class WebViewAccessibilityHandler : CefObject
{
    private readonly WebViewBrowser browser;

    public WebViewAccessibilityHandler(WebViewBrowser browser) : base(sizeof(CefAccessibilityHandler))
    {
        this.browser = browser;
        var handler = (CefAccessibilityHandler*)Native;
        handler->on_accessibility_tree_change = &OnTreeChange;
        handler->on_accessibility_location_change = &OnLocationChange;
    }

    // The value is lent for the call: not released here (measured - releasing it brought CEF down).
    [UnmanagedCallersOnly]
    private static void OnTreeChange(CefAccessibilityHandler* self, CefValue* value)
    {
        byte[] json = null;
        try
        {
            json = Utf8Json(value, out var length);
            if (json != null)
            {
                From<WebViewAccessibilityHandler>(self).browser.OnAccessibilityTreeChange(json.AsSpan(0, length));
            }
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }
        finally
        {
            Give(json);
        }
    }

    [UnmanagedCallersOnly]
    private static void OnLocationChange(CefAccessibilityHandler* self, CefValue* value)
    {
        byte[] json = null;
        try
        {
            json = Utf8Json(value, out var length);
            if (json != null)
            {
                From<WebViewAccessibilityHandler>(self).browser.OnAccessibilityLocationChange(json.AsSpan(0, length));
            }
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }
        finally
        {
            Give(json);
        }
    }

    // The value written by CEF as JSON, in UTF-8, in a buffer from the shared pool; null for none.
    private static byte[] Utf8Json(CefValue* value, out int length)
    {
        length = 0;
        if (value == null)
        {
            return null;
        }

        var written = (CefStringUtf16*)CefInterop.CefWriteJson(value, CefJsonWriterOptions.Default).pointer;
        if (written == null)
        {
            return null;
        }

        try
        {
            var chars = new ReadOnlySpan<char>(written->str, (int)written->length);
            var buffer = ArrayPool<byte>.Shared.Rent(Math.Max(1, Encoding.UTF8.GetByteCount(chars)));
            length = Encoding.UTF8.GetBytes(chars, buffer);
            return buffer;
        }
        finally
        {
            CefInterop.CefStringUserfreeUtf16Free(new CefStringUserfreeUtf16 { pointer = written });
        }
    }

    private static void Give(byte[] buffer)
    {
        if (buffer != null)
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
