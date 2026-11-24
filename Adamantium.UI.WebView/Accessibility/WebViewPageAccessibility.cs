using System.Text.Json;
using Adamantium.Mathematics;
using Adamantium.UI.Core;

namespace Adamantium.UI.WebView.Accessibility;

/// <summary>
/// The page's accessibility as Chromium tells it, kept up to date from its JSON: the document's tree and its frames',
/// which nodes are shown and where they stand. Shown are the nodes a reader names - a link, a field, a heading, a run of
/// text - as Chrome shows them: what Chromium ignores, nameless boxes around other nodes and the text repeating a
/// node's own name are passed over, their children shown in their place. The JSON is read in one pass as it comes - a
/// heavy page's first tree is megabytes - each field taken by its name's bytes, what is not needed skipped whole.
/// </summary>
internal sealed class WebViewPageAccessibility
{
    private readonly Dictionary<string, WebViewAccessibilityTree> trees = new();
    private readonly Dictionary<string, WebViewAccessibilityNode> frameHosts = new();
    private readonly List<WebViewAccessibilityNode> nodesRead = [];
    private readonly List<ToldEvent> eventsRead = [];
    private readonly List<int> idsRead = [];
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

    /// <summary>Takes in a change of a tree, as UTF-8 JSON: <c>ax_tree_id</c>, its <c>updates</c> - each with the nodes as
    /// they are now - and the <c>events</c> that came with them. False when it changed nothing: late for a tree the
    /// page has left, or not a change at all.</summary>
    public bool ApplyTreeChange(ReadOnlySpan<byte> json)
    {
        Events.Clear();
        // Chromium writes the keys in order, the tree's id first; looked for apart, should it ever not.
        if (TreeIdOf(json) is not { } treeId || retired.Contains(treeId))
        {
            return false;
        }

        var reader = new Utf8JsonReader(json, new JsonReaderOptions { MaxDepth = 256 });
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            return false;
        }

        var focusedBefore = Focused;
        var tree = trees.GetValueOrDefault(treeId) ?? (trees[treeId] = new WebViewAccessibilityTree(treeId));
        eventsRead.Clear();

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("updates"u8))
            {
                reader.Read();
                ReadUpdates(ref reader, tree);
            }
            else if (reader.ValueTextEquals("events"u8))
            {
                reader.Read();
                ReadEvents(ref reader);
            }
            else
            {
                reader.Read();
                reader.Skip();
            }
        }

        foreach (var told in eventsRead)
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

    /// <summary>Takes in nodes that moved, as UTF-8 JSON: for each, its tree, its id and where it stands now. False when
    /// it was no list of moves.</summary>
    public bool ApplyLocationChange(ReadOnlySpan<byte> json)
    {
        Events.Clear();
        var reader = new Utf8JsonReader(json, new JsonReaderOptions { MaxDepth = 256 });
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
        {
            return false;
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
        {
            string treeId = null;
            var id = 0;
            var place = new Place();
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("ax_tree_id"u8))
                {
                    reader.Read();
                    treeId = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                }
                else if (reader.ValueTextEquals("id"u8))
                {
                    reader.Read();
                    id = Int(ref reader);
                }
                else if (reader.ValueTextEquals("new_location"u8))
                {
                    reader.Read();
                    ReadPlace(ref reader, ref place, "bounds"u8);
                }
                else
                {
                    reader.Read();
                    reader.Skip();
                }
            }

            if (treeId != null && trees.TryGetValue(treeId, out var tree) && tree.Find(id) is { } node)
            {
                place.ApplyTo(node);
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

    private static string TreeIdOf(ReadOnlySpan<byte> json)
    {
        var reader = new Utf8JsonReader(json, new JsonReaderOptions { MaxDepth = 256 });
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            return null;
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var isTreeId = reader.ValueTextEquals("ax_tree_id"u8);
            reader.Read();
            if (isTreeId)
            {
                return reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
            }

            reader.Skip();
        }

        return null;
    }

    private void ReadUpdates(ref Utf8JsonReader reader, WebViewAccessibilityTree tree)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            reader.Skip();
            return;
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
        {
            nodesRead.Clear();
            leaving.Clear();
            WebViewAccessibilityNode formerRoot = null;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("nodes"u8))
                {
                    reader.Read();
                    ReadNodes(ref reader, tree);
                }
                else if (reader.ValueTextEquals("node_id_to_clear"u8))
                {
                    reader.Read();
                    if (tree.Find(Int(ref reader)) is { } cleared)
                    {
                        NoteLeaving(cleared, cleared.ChildIds, []);
                        cleared.ChildIds = [];
                    }
                }
                else if (reader.ValueTextEquals("root_id"u8))
                {
                    reader.Read();
                    if (Int(ref reader) is var rootId and not 0)
                    {
                        formerRoot = rootId != tree.RootId ? tree.Root : null;
                        tree.RootId = rootId;
                    }
                }
                else if (reader.ValueTextEquals("tree_data"u8))
                {
                    reader.Read();
                    ReadTreeData(ref reader, tree);
                }
                else
                {
                    reader.Read();
                    reader.Skip();
                }
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

    private void ReadEvents(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            reader.Skip();
            return;
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
        {
            string kind = null;
            var id = 0;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("event_type"u8))
                {
                    reader.Read();
                    kind = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                }
                else if (reader.ValueTextEquals("id"u8))
                {
                    reader.Read();
                    id = Int(ref reader);
                }
                else
                {
                    reader.Read();
                    reader.Skip();
                }
            }

            if (kind != null)
            {
                eventsRead.Add(new ToldEvent(kind, id));
            }
        }
    }

    private void ReadTreeData(ref Utf8JsonReader reader, WebViewAccessibilityTree tree)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return;
        }

        string parentTreeId = null, focusedTree = null;
        var focusId = 0;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("focus_id"u8))
            {
                reader.Read();
                focusId = Int(ref reader);
            }
            else if (reader.ValueTextEquals("parent_tree_id"u8))
            {
                reader.Read();
                parentTreeId = Text(ref reader);
            }
            else if (reader.ValueTextEquals("focused_tree_id"u8))
            {
                reader.Read();
                focusedTree = Text(ref reader);
            }
            else
            {
                reader.Read();
                reader.Skip();
            }
        }

        tree.FocusId = focusId;
        tree.ParentTreeId = parentTreeId is { Length: > 0 } ? parentTreeId : null;
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

        focusedTreeId = focusedTree;
    }

    private void ReadNodes(ref Utf8JsonReader reader, WebViewAccessibilityTree tree)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            reader.Skip();
            return;
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
        {
            // Its id comes after most of what it says: what is read waits for it.
            var fields = new NodeFields();
            var place = new Place();
            idsRead.Clear();
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("id"u8))
                {
                    reader.Read();
                    fields.Id = Int(ref reader);
                }
                else if (reader.ValueTextEquals("role"u8))
                {
                    reader.Read();
                    fields.Role = Role(ref reader);
                }
                else if (reader.ValueTextEquals("attributes"u8))
                {
                    reader.Read();
                    ReadAttributes(ref reader, ref fields);
                }
                else if (reader.ValueTextEquals("child_ids"u8))
                {
                    reader.Read();
                    ReadIds(ref reader);
                }
                else if (reader.ValueTextEquals("state"u8))
                {
                    reader.Read();
                    ReadStates(ref reader, ref fields);
                }
                else if (reader.ValueTextEquals("location"u8))
                {
                    reader.Read();
                    place.Bounds = ReadRect(ref reader);
                }
                else if (reader.ValueTextEquals("offset_container_id"u8))
                {
                    reader.Read();
                    place.OffsetContainerId = Int(ref reader);
                }
                else if (reader.ValueTextEquals("transform"u8))
                {
                    reader.Read();
                    place.Transform = Text(ref reader);
                }
                else
                {
                    reader.Read();
                    reader.Skip();
                }
            }

            // Chromium numbers no node 0: one said with no id is no node.
            if (fields.Id == 0)
            {
                continue;
            }

            var node = tree.Take(fields.Id);
            var childIds = idsRead.ToArray();
            NoteLeaving(node, node.ChildIds, childIds);
            // A frame that went somewhere else, or went: the tree it showed goes.
            if (node.ChildTreeId != null && node.ChildTreeId != (fields.ChildTreeId is { Length: > 0 } next ? next : null))
            {
                ForgetFrame(node.ChildTreeId);
            }

            fields.ApplyTo(node, childIds);
            place.ApplyTo(node);
            if (node.ChildTreeId != null)
            {
                frameHosts[node.ChildTreeId] = node;
            }

            nodesRead.Add(node);
        }
    }

    private static void ReadAttributes(ref Utf8JsonReader reader, ref NodeFields fields)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return;
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("name"u8)) { reader.Read(); fields.Name = Text(ref reader); }
            else if (reader.ValueTextEquals("value"u8)) { reader.Read(); fields.Value = Text(ref reader); }
            else if (reader.ValueTextEquals("description"u8)) { reader.Read(); fields.Description = Text(ref reader); }
            else if (reader.ValueTextEquals("htmlId"u8)) { reader.Read(); fields.HtmlId = Text(ref reader); }
            else if (reader.ValueTextEquals("htmlTag"u8)) { reader.Read(); fields.HtmlTag = Text(ref reader); }
            else if (reader.ValueTextEquals("inputType"u8)) { reader.Read(); fields.InputType = Text(ref reader); }
            else if (reader.ValueTextEquals("nameFrom"u8)) { reader.Read(); fields.NameFrom = Text(ref reader); }
            else if (reader.ValueTextEquals("checkedState"u8)) { reader.Read(); fields.CheckedState = Text(ref reader); }
            else if (reader.ValueTextEquals("restriction"u8)) { reader.Read(); fields.Restriction = Text(ref reader); }
            else if (reader.ValueTextEquals("childTreeId"u8)) { reader.Read(); fields.ChildTreeId = Text(ref reader); }
            else if (reader.ValueTextEquals("scrollX"u8)) { reader.Read(); fields.ScrollX = Number(ref reader); }
            else if (reader.ValueTextEquals("scrollY"u8)) { reader.Read(); fields.ScrollY = Number(ref reader); }
            else
            {
                reader.Read();
                reader.Skip();
            }
        }
    }

    private static void ReadStates(ref Utf8JsonReader reader, ref NodeFields fields)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            reader.Skip();
            return;
        }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                reader.Skip();
            }
            else if (reader.ValueTextEquals("focusable"u8)) fields.IsFocusable = true;
            else if (reader.ValueTextEquals("editable"u8)) fields.IsEditable = true;
            else if (reader.ValueTextEquals("invisible"u8)) fields.IsInvisible = true;
            else if (reader.ValueTextEquals("ignored"u8)) fields.IsIgnored = true;
            else if (reader.ValueTextEquals("collapsed"u8)) fields.IsCollapsed = true;
            else if (reader.ValueTextEquals("expanded"u8)) fields.IsExpanded = true;
        }
    }

    private void ReadIds(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            reader.Skip();
            return;
        }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            idsRead.Add(Int(ref reader));
        }
    }

    // A place: its rectangle (under "location" in a node, under "bounds" in a moved one), container and transform.
    private static void ReadPlace(ref Utf8JsonReader reader, ref Place place, ReadOnlySpan<byte> boundsName)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return;
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals(boundsName))
            {
                reader.Read();
                place.Bounds = ReadRect(ref reader);
            }
            else if (reader.ValueTextEquals("offset_container_id"u8))
            {
                reader.Read();
                place.OffsetContainerId = Int(ref reader);
            }
            else if (reader.ValueTextEquals("transform"u8))
            {
                reader.Read();
                place.Transform = Text(ref reader);
            }
            else
            {
                reader.Read();
                reader.Skip();
            }
        }
    }

    private static Rect ReadRect(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return Rect.Empty;
        }

        double x = 0, y = 0, width = 0, height = 0;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("x"u8)) { reader.Read(); x = Number(ref reader); }
            else if (reader.ValueTextEquals("y"u8)) { reader.Read(); y = Number(ref reader); }
            else if (reader.ValueTextEquals("width"u8)) { reader.Read(); width = Number(ref reader); }
            else if (reader.ValueTextEquals("height"u8)) { reader.Read(); height = Number(ref reader); }
            else
            {
                reader.Read();
                reader.Skip();
            }
        }

        return new Rect(x, y, width, height);
    }

    private static string Text(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString();
        }

        reader.Skip();
        return null;
    }

    // The roles most nodes have, one string each for the whole page rather than one per node.
    private static readonly string[] CommonRoles =
    [
        "genericContainer", "staticText", "inlineTextBox", "paragraph", "link", "listItem", "list", "image", "heading",
        "button", "textField", "cell", "row", "table", "group", "section", "labelText", "listMarker", "none",
        "checkBox", "rootWebArea", "navigation", "main", "article", "banner", "contentInfo", "form", "region"
    ];

    private static readonly byte[][] CommonRoleBytes = CommonRoles.Select(System.Text.Encoding.UTF8.GetBytes).ToArray();

    private static string Role(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            return Text(ref reader);
        }

        for (var i = 0; i < CommonRoleBytes.Length; i++)
        {
            if (reader.ValueTextEquals(CommonRoleBytes[i]))
            {
                return CommonRoles[i];
            }
        }

        return reader.GetString();
    }

    private static double Number(ref Utf8JsonReader reader) =>
        reader.TokenType == JsonTokenType.Number && reader.TryGetDouble(out var number) ? number : 0;

    private static int Int(ref Utf8JsonReader reader) =>
        reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number) ? number : 0;

    private readonly record struct ToldEvent(string Kind, int Id);

    private readonly record struct LeavingChild(WebViewAccessibilityNode Holder, int ChildId);

    // What a node says, held until its id is read.
    private struct NodeFields
    {
        public int Id;
        public string Role, Name, Value, Description, HtmlId, HtmlTag, InputType, NameFrom, CheckedState, Restriction,
            ChildTreeId;
        public double ScrollX, ScrollY;
        public bool IsFocusable, IsEditable, IsInvisible, IsIgnored, IsCollapsed, IsExpanded;

        // A node is said whole each time: what is not said now is not so any longer.
        public readonly void ApplyTo(WebViewAccessibilityNode node, int[] childIds)
        {
            node.Role = Role ?? string.Empty;
            node.Name = Name ?? string.Empty;
            node.Value = Value ?? string.Empty;
            node.Description = Description ?? string.Empty;
            node.HtmlId = HtmlId ?? string.Empty;
            node.HtmlTag = HtmlTag ?? string.Empty;
            node.InputType = InputType ?? string.Empty;
            node.NameFrom = NameFrom ?? string.Empty;
            node.CheckedState = CheckedState ?? string.Empty;
            node.Restriction = Restriction ?? string.Empty;
            node.ChildTreeId = ChildTreeId is { Length: > 0 } ? ChildTreeId : null;
            node.Scroll = new Vector2<double>(ScrollX, ScrollY);
            node.IsFocusable = IsFocusable;
            node.IsEditable = IsEditable;
            node.IsInvisible = IsInvisible;
            node.IsIgnored = IsIgnored;
            node.IsCollapsed = IsCollapsed;
            node.IsExpanded = IsExpanded;
            node.ChildIds = childIds;
        }
    }

    private struct Place
    {
        public Rect Bounds;
        public int OffsetContainerId;
        public string Transform;

        public readonly void ApplyTo(WebViewAccessibilityNode node)
        {
            node.Location = Bounds;
            node.OffsetContainerId = OffsetContainerId;
            node.Transform = WebViewAccessibilityTransform.Parse(Transform);
        }
    }
}
