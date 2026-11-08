namespace Adamantium.UI.WebView;

/// <summary>What CEF is initialized with, once for the process: the profile every <see cref="WebView"/> shares - its
/// cache, cookies and storage - and the language pages are asked in.</summary>
public sealed class WebViewOptions
{
    /// <summary>The folder of the profile; null takes a WebView folder in <see cref="ApplicationData.Folder"/>.</summary>
    public string ProfileFolder { get; set; }

    /// <summary>The language pages are asked in, as "en-US"; null takes the system's.</summary>
    public string Locale { get; set; }
}
