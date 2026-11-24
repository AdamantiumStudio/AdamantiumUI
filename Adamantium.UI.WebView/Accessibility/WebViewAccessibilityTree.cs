namespace Adamantium.UI.WebView.Accessibility;

/// <summary>One accessibility tree Chromium keeps - the page's document, or a frame's inside it: its nodes by id, its
/// root, and the node holding the focus.</summary>
internal sealed class WebViewAccessibilityTree
{
    public WebViewAccessibilityTree(string id)
    {
        Id = id;
    }

    public string Id { get; }

    /// <summary>The frame's tree this one is shown in; null for the page's own.</summary>
    public string ParentTreeId { get; set; }

    public int RootId { get; set; }

    public int FocusId { get; set; }

    public Dictionary<int, WebViewAccessibilityNode> Nodes { get; } = new();

    public WebViewAccessibilityNode Root => Find(RootId);

    public WebViewAccessibilityNode Find(int id) => Nodes.GetValueOrDefault(id);

    /// <summary>The node <paramref name="id"/>, made now if Chromium names it for the first time.</summary>
    public WebViewAccessibilityNode Take(int id)
    {
        if (!Nodes.TryGetValue(id, out var node))
        {
            node = new WebViewAccessibilityNode(this, id);
            Nodes[id] = node;
        }

        return node;
    }
}
