namespace Adamantium.UI.WebView.Accessibility;

/// <summary>Something said to have happened to the node <paramref name="Id"/>.</summary>
internal readonly record struct WebViewAccessibilityToldEvent(string Kind, int Id);
