using Adamantium.UI.Core;

namespace Adamantium.UI.WebView.Accessibility;

/// <summary>A change of one of the page's accessibility trees as Chromium said it, read off the UI loop and taken in on
/// it: the tree's updates, in order, and the events that came with them.</summary>
internal sealed class WebViewAccessibilityChange
{
    public WebViewAccessibilityChange(string treeId)
    {
        TreeId = treeId;
    }

    public string TreeId { get; }

    public List<WebViewAccessibilityUpdate> Updates { get; } = [];

    public List<WebViewAccessibilityToldEvent> Events { get; } = [];
}

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

/// <summary>What a tree's data says that is kept.</summary>
internal sealed record WebViewAccessibilityTreeData(int FocusId, string ParentTreeId, string FocusedTreeId);

/// <summary>Something said to have happened to the node <paramref name="Id"/>.</summary>
internal readonly record struct WebViewAccessibilityToldEvent(string Kind, int Id);

/// <summary>A node as Chromium said it, whole: what is not said now is not so any longer.</summary>
internal struct WebViewAccessibilityNodeData
{
    public int Id;
    public string Role, Name, Value, Description, HtmlId, HtmlTag, InputType, NameFrom, CheckedState, Restriction,
        ChildTreeId;
    public double ScrollX, ScrollY;
    public bool IsFocusable, IsEditable, IsInvisible, IsIgnored, IsCollapsed, IsExpanded;
    public int[] ChildIds;
    public WebViewAccessibilityPlace Place;
}

/// <summary>Where a node stands: its rectangle against its container, the container, and a transform.</summary>
internal struct WebViewAccessibilityPlace
{
    public Rect Bounds;
    public int OffsetContainerId;
    public string Transform;
}

/// <summary>A node that moved: its tree, its id, and where it stands now.</summary>
internal readonly record struct WebViewAccessibilityMove(string TreeId, int Id, WebViewAccessibilityPlace Place);
