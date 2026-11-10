namespace Adamantium.UI.WebView;

/// <summary>Why a page did not load: Chromium's error code (net error, negative), its name for it, and the address that
/// failed; or <see cref="EngineUnavailable"/>, when no page can load at all.</summary>
public sealed record WebViewLoadError(int Code, string Text, string Address)
{
    /// <summary>The code of a page that cannot load because CEF is not beside the application - native/fetch-cef
    /// stages it - or refused to start. Positive: Chromium's own codes are negative.</summary>
    public const int EngineUnavailable = 1;
}
