using System.Runtime.InteropServices;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>What Chromium says of the page's accessibility tree, once it was asked to keep one: its nodes as they change,
/// and where they moved - handed to the engine's <see cref="WebViewAccessibilityReading"/>, which reads it off the UI
/// loop for the browser's copy of the tree.</summary>
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

    // The value is lent for the call (measured - releasing it brought CEF down): the reading takes a reference of its own.
    [UnmanagedCallersOnly]
    private static void OnTreeChange(CefAccessibilityHandler* self, CefValue* value)
    {
        try
        {
            From<WebViewAccessibilityHandler>(self).browser.OnAccessibilityChange(value, true);
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }
    }

    [UnmanagedCallersOnly]
    private static void OnLocationChange(CefAccessibilityHandler* self, CefValue* value)
    {
        try
        {
            From<WebViewAccessibilityHandler>(self).browser.OnAccessibilityChange(value, false);
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }
    }
}
