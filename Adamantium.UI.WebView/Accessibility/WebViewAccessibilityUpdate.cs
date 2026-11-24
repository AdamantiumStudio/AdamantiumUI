namespace Adamantium.UI.WebView.Accessibility;

/// <summary>One update of a tree: a node whose children go, the tree's root and data when they changed, and nodes as they
/// are now.</summary>
internal sealed class WebViewAccessibilityUpdate
{
    /// <summary>The node whose children are let go of before the nodes are taken in; 0 for none.</summary>
    public int NodeIdToClear { get; set; }

    /// <summary>The tree's root now; 0 when it did not say.</summary>
    public int RootId { get; set; }

    /// <summary>The tree's data - its focus, the frame's tree it is shown in; null when it did not say.</summary>
    public WebViewAccessibilityTreeData TreeData { get; set; }

    public List<WebViewAccessibilityNodeData> Nodes { get; } = [];
}
