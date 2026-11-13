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

    /// <summary>The color scheme pages are asked in; the system's by default. In the system's dark one Chromium paints
    /// about:blank dark. Read for each page as it is made, and asked again once its DevTools close. Only the page is
    /// asked: not a frame of another site inside it, drawn by a process of its own, nor the DevTools beside it.</summary>
    public WebViewColorScheme ColorScheme { get; set; }
}
