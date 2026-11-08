namespace Adamantium.UI.WebView;

/// <summary>Why a page did not load: Chromium's error code (net error, negative), its name for it, and the address that
/// failed.</summary>
public sealed record WebViewLoadError(int Code, string Text, string Address);
