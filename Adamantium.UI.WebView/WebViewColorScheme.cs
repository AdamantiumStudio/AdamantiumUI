namespace Adamantium.UI.WebView;

/// <summary>The color scheme pages are asked in - what their <c>prefers-color-scheme</c> reads - and the one Chromium
/// paints a page with no colors of its own in, as about:blank.</summary>
public enum WebViewColorScheme
{
    /// <summary>The system's, light or dark as it is set.</summary>
    System,

    /// <summary>Light, whatever the system's.</summary>
    Light,

    /// <summary>Dark, whatever the system's.</summary>
    Dark
}
