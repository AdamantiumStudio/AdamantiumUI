namespace Adamantium.UI.WebView.Accessibility;

/// <summary>Something Chromium says happened to a node with a change of the tree - <c>focus</c>, <c>valueChanged</c>,
/// <c>checkedStateChanged</c>, <c>childrenChanged</c>, <c>loadComplete</c>...</summary>
internal readonly record struct WebViewAccessibilityEvent(string Kind, WebViewAccessibilityNode Node);
