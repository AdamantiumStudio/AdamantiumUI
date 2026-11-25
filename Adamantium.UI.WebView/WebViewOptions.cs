namespace Adamantium.UI.WebView;

/// <summary>The application's CEF: what it is initialized with, once for the process - the profile every
/// <see cref="WebView"/> shares, its cache, cookies and storage, and the language pages are asked in - and the color
/// scheme each page is asked in as it is made.</summary>
public sealed class WebViewOptions
{
    /// <summary>The folder of the profile; null takes a WebView folder in <see cref="ApplicationData.Folder"/>.</summary>
    public string ProfileFolder { get; set; }

    /// <summary>The language pages are asked in, as "en-US"; null takes the system's.</summary>
    public string Locale { get; set; }

    /// <summary>The color scheme pages are asked in at first; the application's by default. Changed later through
    /// <see cref="WebViewEngine.ColorScheme"/>. The page is asked directly; Chromium's own scheme - about:blank, its own
    /// pages, and what else of the profile follows it, as a frame of another site or the DevTools - is set to match.</summary>
    public WebViewColorScheme ColorScheme { get; set; } = WebViewColorScheme.Application;

    /// <summary>The components Chromium may fetch from Google and keep up to date; none by default, so nothing is
    /// fetched that the application did not ask for. A component left out is not loaded either, though an earlier run
    /// fetched it.</summary>
    public WebViewComponents Components { get; set; }
}
