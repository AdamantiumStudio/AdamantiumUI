using Adamantium.Mathematics;
using Adamantium.UI.Core;

namespace Adamantium.UI.WebView.Accessibility;

/// <summary>
/// The page's accessibility as Chromium tells it, kept up to date from its JSON: the document's tree and its frames',
/// which nodes are shown and where they stand. Shown are the nodes a reader names - a link, a field, a heading, a run of
/// text - as Chrome shows them: what Chromium ignores, nameless boxes around other nodes and the text repeating a
/// node's own name are passed over, their children shown in their place. What it takes in is
/// read off the UI loop by <see cref="WebViewAccessibilityReader"/>; this, on it, only lays it out.
/// </summary>
internal sealed class WebViewPageAccessibility
{
    private readonly Dictionary<string, WebViewAccessibilityTree> trees = new();
    private readonly Dictionary<string, WebViewAccessibilityNode> frameHosts = new();
    private readonly List<WebViewAccessibilityNode> nodesRead = [];
    private readonly List<LeavingChild> leaving = [];
    private readonly Queue<string> retiredOrder = new();
    private readonly HashSet<string> retired = [];
    private string focusedTreeId;
    private Vector2<double>? documentScroll;

    // Trees of documents and frames the page has left: what still comes for them is late, and not taken in. The last
    // few are remembered - a late update comes soon after or not at all.
    private const int RetiredRemembered = 64;

    /// <summary>The page's own tree; null before Chromium told it.</summary>
    public WebViewAccessibilityTree Document { get; private set; }

    /// <summary>What Chromium said happened with the last change of a tree, its nodes as they are now.</summary>
    public List<WebViewAccessibilityEvent> Events { get; } = [];

    /// <summary>The node holding the page's focus; null when none does.</summary>
    public WebViewAccessibilityNode Focused =>
        focusedTreeId != null && trees.TryGetValue(focusedTreeId, out var tree) ? tree.Find(tree.FocusId) : Document?.Find(Document.FocusId);

    /// <summary>Takes the document's scroll as the page tells it, in the view's pixels: the tree's own word on it goes
    /// stale as the page scrolls.</summary>
    public void ScrollDocument(Vector2<double> scroll)
    {
        Events.Clear();
        documentScroll = scroll;
    }

    /// <summary>The document's scroll as the page last told it; null before it told any.</summary>
    public Vector2<double>? DocumentScroll => documentScroll;

    /// <summary>Whether <paramref name="node"/> is still the page's: in a tree of the document shown, not taken out.</summary>
    public bool Holds(WebViewAccessibilityNode node) =>
        trees.TryGetValue(node.Tree.Id, out var tree) && ReferenceEquals(tree, node.Tree) && tree.Find(node.Id) == node;

    /// <summary>Takes in a change of a tree as read: its updates, each with the nodes as they are now, and the events that
    /// came with them. False when it changed nothing: late for a tree the page has left, or not a change at all.</summary>
    public bool Apply(WebViewAccessibilityChange change)
    {
        Events.Clear();
        if (change == null || retired.Contains(change.TreeId))
        {
            return false;
        }

        var focusedBefore = Focused;
        var tree = trees.GetValueOrDefault(change.TreeId) ?? (trees[change.TreeId] = new WebViewAccessibilityTree(change.TreeId));
        foreach (var update in change.Updates)
        {
            Apply(update, tree);
        }

        foreach (var told in change.Events)
        {
            if (tree.Find(told.Id) is { } node)
            {
                Events.Add(new WebViewAccessibilityEvent(told.Kind, node));
            }
        }

        // The focus moving is told by the tree's data as often as by an event: told either way, once.
        if (Focused is { } focused && focused != focusedBefore && !Events.Any(told => told.Kind == "focus"))
        {
            Events.Add(new WebViewAccessibilityEvent("focus", focused));
        }

        return true;
    }

    /// <summary>Takes in nodes that moved, as read: for each, its tree, its id and where it stands now. False when it was
    /// no list of moves.</summary>
    public bool Apply(List<WebViewAccessibilityMove> moves)
    {
        Events.Clear();
        if (moves == null)
        {
            return false;
        }

        foreach (var move in moves)
        {
            if (trees.TryGetValue(move.TreeId, out var tree) && tree.Find(move.Id) is { } node)
            {
                Place(node, move.Place);
            }
        }

        return true;
    }

    /// <summary>The node <paramref name="node"/> is shown under: its nearest holder that is shown, the node of the frame
    /// a frame's document is shown in; null for the page's document.</summary>
    public WebViewAccessibilityNode ShownParent(WebViewAccessibilityNode node)
    {
        for (var steps = 0; steps < 1024; steps++)
        {
            var holder = node.Parent ?? (node.Tree.ParentTreeId != null ? frameHosts.GetValueOrDefault(node.Tree.Id) : null);
            if (holder == null || !IsPassedOver(holder))
            {
                return holder;
            }

            node = holder;
        }

        return null;
    }

    /// <summary>What <paramref name="parent"/> shows as its children, in order.</summary>
    public List<WebViewAccessibilityNode> ShownChildren(WebViewAccessibilityNode parent)
    {
        var shown = new List<WebViewAccessibilityNode>();
        var waiting = new Stack<WebViewAccessibilityNode>();
        PushChildren(parent, waiting);
        while (waiting.Count > 0)
        {
            var node = waiting.Pop();
            if (IsLeftOut(node, parent))
            {
                continue;
            }

            if (IsPassedOver(node))
            {
                PushChildren(node, waiting);
                continue;
            }

            shown.Add(node);
        }

        return shown;
    }

    /// <summary>Where <paramref name="node"/> stands in the view, in its pixels.</summary>
    public Rect PageRect(WebViewAccessibilityNode node)
    {
        // As Chromium places it: its own transform, then its container's place less what the container scrolled, up to
        // the tree's top - and a frame's tree from the node showing it. Bounded, should the chain ever loop.
        var rect = node.Location;
        for (var steps = 0; node != null && steps < 256; steps++)
        {
            if (node.Transform is { } transform)
            {
                rect = transform.Apply(rect);
            }

            var container = node.OffsetContainerId != 0 ? node.Tree.Find(node.OffsetContainerId) : null;
            if (container == null && node.Tree.ParentTreeId != null)
            {
                container = frameHosts.GetValueOrDefault(node.Tree.Id);
                if (container != null)
                {
                    rect = Move(rect, container.Location.X, container.Location.Y);
                    node = container;
                    continue;
                }
            }

            if (container == null)
            {
                break;
            }

            // The document's own scroll as the page last told it; any other box's as the tree says.
            var scroll = documentScroll is { } told && container == Document?.Root ? told : container.Scroll;
            rect = Move(rect, container.Location.X - scroll.X, container.Location.Y - scroll.Y);
            node = container;
        }

        return rect;
    }

    private static Rect Move(Rect rect, double x, double y) => new(rect.X + x, rect.Y + y, rect.Width, rect.Height);

    private void PushChildren(WebViewAccessibilityNode node, Stack<WebViewAccessibilityNode> waiting)
    {
        // Pushed last to first, so they come off in order; a frame's document after the node's own children.
        if (node.ChildTreeId != null && trees.TryGetValue(node.ChildTreeId, out var frame) && frame.Root is { } frameRoot)
        {
            waiting.Push(frameRoot);
        }

        for (var i = node.ChildIds.Length - 1; i >= 0; i--)
        {
            if (node.Tree.Find(node.ChildIds[i]) is { } child)
            {
                waiting.Push(child);
            }
        }
    }

    // Not shown, nor anything inside: pieces of text runs and list markers, text repeating its holder's name, and the
    // insides of a field - its value says what they hold.
    private static bool IsLeftOut(WebViewAccessibilityNode node, WebViewAccessibilityNode holder) =>
        node.Role is "inlineTextBox" or "listMarker" ||
        holder.Role is "textField" or "searchBox" or "textFieldWithComboBox" ||
        node.Role == "staticText" && holder.NameFrom == "contents" && holder.Name.Length > 0;

    // Not shown, its children shown in its place: what Chromium ignores, and nameless boxes and labels - but not a box
    // that takes the focus or typing, as an editor made of a box does.
    private static bool IsPassedOver(WebViewAccessibilityNode node) =>
        node.IsIgnored || node.Role is "none" or "presentational" ||
        node.Role is "genericContainer" or "group" or "section" or "labelText" && node.Name.Length == 0 &&
        !node.IsFocusable && !node.IsEditable;

    // In the order CEF writes an update: the node cleared, the nodes, then the root and the tree's data - a new document's
    // frames are its own before the old document goes.
    private void Apply(WebViewAccessibilityUpdate update, WebViewAccessibilityTree tree)
    {
        nodesRead.Clear();
        leaving.Clear();
        if (update.NodeIdToClear != 0 && tree.Find(update.NodeIdToClear) is { } cleared)
        {
            NoteLeaving(cleared, cleared.ChildIds, []);
            cleared.ChildIds = [];
        }

        foreach (var said in update.Nodes)
        {
            Apply(said, tree);
        }

        WebViewAccessibilityNode formerRoot = null;
        if (update.RootId != 0)
        {
            formerRoot = update.RootId != tree.RootId ? tree.Root : null;
            tree.RootId = update.RootId;
        }

        if (update.TreeData != null)
        {
            Apply(update.TreeData, tree);
        }

        // Once the update's nodes are all in: each child named knows its holder - and a child its holder no longer
        // names, nor any other node of the update, goes with what it holds.
        foreach (var holder in nodesRead)
        {
            foreach (var childId in holder.ChildIds)
            {
                if (tree.Find(childId) is { } child)
                {
                    child.Parent = holder;
                }
            }
        }

        foreach (var gone in leaving)
        {
            if (tree.Find(gone.ChildId) is { } child && child.Parent == gone.Holder &&
                Array.IndexOf(gone.Holder.ChildIds, gone.ChildId) < 0)
            {
                Forget(child);
            }
        }

        // A root another took the place of goes too, unless the update put it under a node of its own.
        if (formerRoot != null && formerRoot != tree.Root && formerRoot.Parent == null && tree.Find(formerRoot.Id) == formerRoot)
        {
            Forget(formerRoot);
        }
    }

    // The children of <holder> it named before and names no longer, to be let go of once the update is all in.
    private void NoteLeaving(WebViewAccessibilityNode holder, int[] before, int[] now)
    {
        foreach (var childId in before)
        {
            if (Array.IndexOf(now, childId) < 0)
            {
                leaving.Add(new LeavingChild(holder, childId));
            }
        }
    }

    // Lets go of a node and all it holds, and of the frames shown in any of them.
    private void Forget(WebViewAccessibilityNode node)
    {
        var waiting = new Stack<WebViewAccessibilityNode>();
        waiting.Push(node);
        while (waiting.Count > 0)
        {
            var current = waiting.Pop();
            current.Tree.Nodes.Remove(current.Id);
            if (current.ChildTreeId != null)
            {
                ForgetFrame(current.ChildTreeId);
            }

            foreach (var childId in current.ChildIds)
            {
                if (current.Tree.Find(childId) is { } child && child.Parent == current)
                {
                    waiting.Push(child);
                }
            }
        }
    }

    // Lets go of a frame's tree and of the frames inside it, remembered as left.
    private void ForgetFrame(string treeId)
    {
        var waiting = new Stack<string>();
        waiting.Push(treeId);
        while (waiting.Count > 0)
        {
            var id = waiting.Pop();
            frameHosts.Remove(id);
            Retire(id);
            if (!trees.Remove(id, out var tree))
            {
                continue;
            }

            foreach (var inner in tree.Nodes.Values)
            {
                if (inner.ChildTreeId != null)
                {
                    waiting.Push(inner.ChildTreeId);
                }
            }
        }
    }

    // Lets go of a document left and of every tree under it, frames of frames too.
    private void RetireDocument(string documentId)
    {
        var leftIds = new HashSet<string> { documentId };
        bool grew;
        do
        {
            grew = false;
            foreach (var other in trees.Values)
            {
                if (other.ParentTreeId != null && leftIds.Contains(other.ParentTreeId) && leftIds.Add(other.Id))
                {
                    grew = true;
                }
            }
        }
        while (grew);

        foreach (var id in leftIds)
        {
            trees.Remove(id);
            frameHosts.Remove(id);
            Retire(id);
        }

        // Frames of the left document whose trees have not come yet: left too, so they are not taken in when they come.
        foreach (var hosted in frameHosts.Where(pair => leftIds.Contains(pair.Value.Tree.Id)).Select(pair => pair.Key).ToList())
        {
            frameHosts.Remove(hosted);
            trees.Remove(hosted);
            Retire(hosted);
        }
    }

    private void Retire(string treeId)
    {
        if (!retired.Add(treeId))
        {
            return;
        }

        retiredOrder.Enqueue(treeId);
        if (retiredOrder.Count > RetiredRemembered)
        {
            retired.Remove(retiredOrder.Dequeue());
        }
    }

    private void Apply(WebViewAccessibilityTreeData data, WebViewAccessibilityTree tree)
    {
        tree.FocusId = data.FocusId;
        tree.ParentTreeId = data.ParentTreeId;
        if (tree.ParentTreeId != null)
        {
            return;
        }

        // A new document of the page: the old one and its frames' trees go, remembered as left so a late update of theirs
        // is not taken in. A frame of the new one that came before it stays.
        if (!ReferenceEquals(Document, tree))
        {
            if (Document != null)
            {
                RetireDocument(Document.Id);
            }

            Document = tree;
            documentScroll = null;
        }

        focusedTreeId = data.FocusedTreeId;
    }

    private void Apply(in WebViewAccessibilityNodeData said, WebViewAccessibilityTree tree)
    {
        var node = tree.Take(said.Id);
        NoteLeaving(node, node.ChildIds, said.ChildIds);
        // A frame that went somewhere else, or went: the tree it showed goes.
        var childTreeId = said.ChildTreeId is { Length: > 0 } next ? next : null;
        if (node.ChildTreeId != null && node.ChildTreeId != childTreeId)
        {
            ForgetFrame(node.ChildTreeId);
        }

        // A node is said whole each time: what is not said now is not so any longer.
        node.Role = said.Role ?? string.Empty;
        node.Name = said.Name ?? string.Empty;
        node.Value = said.Value ?? string.Empty;
        node.Description = said.Description ?? string.Empty;
        node.HtmlId = said.HtmlId ?? string.Empty;
        node.HtmlTag = said.HtmlTag ?? string.Empty;
        node.InputType = said.InputType ?? string.Empty;
        node.NameFrom = said.NameFrom ?? string.Empty;
        node.CheckedState = said.CheckedState ?? string.Empty;
        node.Restriction = said.Restriction ?? string.Empty;
        node.ChildTreeId = childTreeId;
        node.Scroll = new Vector2<double>(said.ScrollX, said.ScrollY);
        node.IsFocusable = said.IsFocusable;
        node.IsEditable = said.IsEditable;
        node.IsInvisible = said.IsInvisible;
        node.IsIgnored = said.IsIgnored;
        node.IsCollapsed = said.IsCollapsed;
        node.IsExpanded = said.IsExpanded;
        node.ChildIds = said.ChildIds;
        Place(node, said.Place);
        if (node.ChildTreeId != null)
        {
            frameHosts[node.ChildTreeId] = node;
        }

        nodesRead.Add(node);
    }

    private static void Place(WebViewAccessibilityNode node, in WebViewAccessibilityPlace place)
    {
        node.Location = place.Bounds;
        node.OffsetContainerId = place.OffsetContainerId;
        node.Transform = WebViewAccessibilityTransform.Parse(place.Transform);
    }

    private readonly record struct LeavingChild(WebViewAccessibilityNode Holder, int ChildId);
}
