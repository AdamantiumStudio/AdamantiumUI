namespace Adamantium.UI.WebView.Accessibility;

/// <summary>What a tree's data says that is kept.</summary>
internal sealed record WebViewAccessibilityTreeData(int FocusId, string ParentTreeId, string FocusedTreeId);
