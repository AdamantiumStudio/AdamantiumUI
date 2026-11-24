using System.Text;
using System.Text.Json;
using Adamantium.UI.Core;

namespace Adamantium.UI.WebView.Accessibility;

/// <summary>
/// Reads what Chromium says of the page's accessibility tree from its UTF-8 JSON - off the UI loop: a heavy page's first
/// tree is megabytes. One pass as it comes, each field taken by its name's bytes, what is not needed skipped whole, no
/// recursion; the common roles one string each for the whole page.
/// </summary>
internal static class WebViewAccessibilityReader
{
    private static readonly string[] CommonRoles =
    [
        "genericContainer", "staticText", "inlineTextBox", "paragraph", "link", "listItem", "list", "image", "heading",
        "button", "textField", "cell", "row", "table", "group", "section", "labelText", "listMarker", "none",
        "checkBox", "rootWebArea", "navigation", "main", "article", "banner", "contentInfo", "form", "region"
    ];

    private static readonly byte[][] CommonRoleBytes = CommonRoles.Select(Encoding.UTF8.GetBytes).ToArray();

    private static readonly JsonReaderOptions Options = new() { MaxDepth = 256 };

    /// <summary>A change of a tree: <c>ax_tree_id</c>, its <c>updates</c> and <c>events</c>; null when it is none.</summary>
    public static WebViewAccessibilityChange ReadTreeChange(ReadOnlySpan<byte> json)
    {
        // Chromium writes the keys in order, the tree's id first; looked for apart, should it ever not.
        if (TreeIdOf(json) is not { } treeId)
        {
            return null;
        }

        var reader = new Utf8JsonReader(json, Options);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            return null;
        }

        var change = new WebViewAccessibilityChange(treeId);
        var ids = new List<int>();
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("updates"u8))
            {
                reader.Read();
                ReadUpdates(ref reader, change, ids);
            }
            else if (reader.ValueTextEquals("events"u8))
            {
                reader.Read();
                ReadEvents(ref reader, change);
            }
            else
            {
                reader.Read();
                reader.Skip();
            }
        }

        return change;
    }

    /// <summary>Nodes that moved: for each, its tree, its id and where it stands now; null when it is no list.</summary>
    public static List<WebViewAccessibilityMove> ReadMoves(ReadOnlySpan<byte> json)
    {
        var reader = new Utf8JsonReader(json, Options);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
        {
            return null;
        }

        var moves = new List<WebViewAccessibilityMove>();
        while (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
        {
            string treeId = null;
            var id = 0;
            var place = new WebViewAccessibilityPlace();
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("ax_tree_id"u8))
                {
                    reader.Read();
                    treeId = Text(ref reader);
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

            if (treeId != null)
            {
                moves.Add(new WebViewAccessibilityMove(treeId, id, place));
            }
        }

        return moves;
    }

    private static string TreeIdOf(ReadOnlySpan<byte> json)
    {
        var reader = new Utf8JsonReader(json, Options);
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

    private static void ReadUpdates(ref Utf8JsonReader reader, WebViewAccessibilityChange change, List<int> ids)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            reader.Skip();
            return;
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
        {
            var update = new WebViewAccessibilityUpdate();
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("nodes"u8))
                {
                    reader.Read();
                    ReadNodes(ref reader, update, ids);
                }
                else if (reader.ValueTextEquals("node_id_to_clear"u8))
                {
                    reader.Read();
                    update.NodeIdToClear = Int(ref reader);
                }
                else if (reader.ValueTextEquals("root_id"u8))
                {
                    reader.Read();
                    update.RootId = Int(ref reader);
                }
                else if (reader.ValueTextEquals("tree_data"u8))
                {
                    reader.Read();
                    update.TreeData = ReadTreeData(ref reader);
                }
                else
                {
                    reader.Read();
                    reader.Skip();
                }
            }

            change.Updates.Add(update);
        }
    }

    private static void ReadEvents(ref Utf8JsonReader reader, WebViewAccessibilityChange change)
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
                    kind = Text(ref reader);
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
                change.Events.Add(new WebViewAccessibilityToldEvent(kind, id));
            }
        }
    }

    private static WebViewAccessibilityTreeData ReadTreeData(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return null;
        }

        string parentTreeId = null, focusedTreeId = null;
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
                focusedTreeId = Text(ref reader);
            }
            else
            {
                reader.Read();
                reader.Skip();
            }
        }

        return new WebViewAccessibilityTreeData(focusId, parentTreeId is { Length: > 0 } ? parentTreeId : null, focusedTreeId);
    }

    private static void ReadNodes(ref Utf8JsonReader reader, WebViewAccessibilityUpdate update, List<int> ids)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            reader.Skip();
            return;
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
        {
            var node = new WebViewAccessibilityNodeData();
            ids.Clear();
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("id"u8))
                {
                    reader.Read();
                    node.Id = Int(ref reader);
                }
                else if (reader.ValueTextEquals("role"u8))
                {
                    reader.Read();
                    node.Role = Role(ref reader);
                }
                else if (reader.ValueTextEquals("attributes"u8))
                {
                    reader.Read();
                    ReadAttributes(ref reader, ref node);
                }
                else if (reader.ValueTextEquals("child_ids"u8))
                {
                    reader.Read();
                    ReadIds(ref reader, ids);
                }
                else if (reader.ValueTextEquals("state"u8))
                {
                    reader.Read();
                    ReadStates(ref reader, ref node);
                }
                else if (reader.ValueTextEquals("location"u8))
                {
                    reader.Read();
                    node.Place.Bounds = ReadRect(ref reader);
                }
                else if (reader.ValueTextEquals("offset_container_id"u8))
                {
                    reader.Read();
                    node.Place.OffsetContainerId = Int(ref reader);
                }
                else if (reader.ValueTextEquals("transform"u8))
                {
                    reader.Read();
                    node.Place.Transform = Text(ref reader);
                }
                else
                {
                    reader.Read();
                    reader.Skip();
                }
            }

            // Chromium numbers no node 0: one said with no id is no node.
            if (node.Id != 0)
            {
                node.ChildIds = ids.ToArray();
                update.Nodes.Add(node);
            }
        }
    }

    private static void ReadAttributes(ref Utf8JsonReader reader, ref WebViewAccessibilityNodeData node)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return;
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("name"u8)) { reader.Read(); node.Name = Text(ref reader); }
            else if (reader.ValueTextEquals("value"u8)) { reader.Read(); node.Value = Text(ref reader); }
            else if (reader.ValueTextEquals("description"u8)) { reader.Read(); node.Description = Text(ref reader); }
            else if (reader.ValueTextEquals("htmlId"u8)) { reader.Read(); node.HtmlId = Text(ref reader); }
            else if (reader.ValueTextEquals("htmlTag"u8)) { reader.Read(); node.HtmlTag = Text(ref reader); }
            else if (reader.ValueTextEquals("inputType"u8)) { reader.Read(); node.InputType = Text(ref reader); }
            else if (reader.ValueTextEquals("nameFrom"u8)) { reader.Read(); node.NameFrom = Text(ref reader); }
            else if (reader.ValueTextEquals("checkedState"u8)) { reader.Read(); node.CheckedState = Text(ref reader); }
            else if (reader.ValueTextEquals("restriction"u8)) { reader.Read(); node.Restriction = Text(ref reader); }
            else if (reader.ValueTextEquals("childTreeId"u8)) { reader.Read(); node.ChildTreeId = Text(ref reader); }
            else if (reader.ValueTextEquals("scrollX"u8)) { reader.Read(); node.ScrollX = Number(ref reader); }
            else if (reader.ValueTextEquals("scrollY"u8)) { reader.Read(); node.ScrollY = Number(ref reader); }
            else
            {
                reader.Read();
                reader.Skip();
            }
        }
    }

    private static void ReadStates(ref Utf8JsonReader reader, ref WebViewAccessibilityNodeData node)
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
            else if (reader.ValueTextEquals("focusable"u8)) node.IsFocusable = true;
            else if (reader.ValueTextEquals("editable"u8)) node.IsEditable = true;
            else if (reader.ValueTextEquals("invisible"u8)) node.IsInvisible = true;
            else if (reader.ValueTextEquals("ignored"u8)) node.IsIgnored = true;
            else if (reader.ValueTextEquals("collapsed"u8)) node.IsCollapsed = true;
            else if (reader.ValueTextEquals("expanded"u8)) node.IsExpanded = true;
        }
    }

    private static void ReadIds(ref Utf8JsonReader reader, List<int> ids)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            reader.Skip();
            return;
        }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            ids.Add(Int(ref reader));
        }
    }

    // A place: its rectangle (under "location" in a node, under "bounds" in a moved one), container and transform.
    private static void ReadPlace(ref Utf8JsonReader reader, ref WebViewAccessibilityPlace place, ReadOnlySpan<byte> boundsName)
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

    private static string Text(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString();
        }

        reader.Skip();
        return null;
    }

    private static double Number(ref Utf8JsonReader reader) =>
        reader.TokenType == JsonTokenType.Number && reader.TryGetDouble(out var number) ? number : 0;

    private static int Int(ref Utf8JsonReader reader) =>
        reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number) ? number : 0;
}
