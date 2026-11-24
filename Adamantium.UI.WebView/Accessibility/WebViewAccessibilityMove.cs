namespace Adamantium.UI.WebView.Accessibility;

/// <summary>A node that moved: its tree, its id, and where it stands now.</summary>
internal readonly record struct WebViewAccessibilityMove(string TreeId, int Id, WebViewAccessibilityPlace Place);
