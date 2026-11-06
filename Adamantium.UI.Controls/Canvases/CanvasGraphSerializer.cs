using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Controls.Canvases;

/// <summary>Reads and writes a canvas's graph and camera as versioned JSON; wires refer to nodes and sockets by name, never
/// by index.</summary>
public static class CanvasGraphSerializer
{
    /// <summary>2 added the comment frames: a reader of 1 would load such a file and drop them.</summary>
    public const int Version = 2;

    /// <summary>Writes the graph the canvas is showing: its nodes, the wires between them and the comment frames.</summary>
    /// <param name="describe">What the application knows about a node that the engine does not: its kind and its
    /// payload. Not given, every node is saved as the plain shape it is.</param>
    public static string Save(NodeGraph canvas, Func<CanvasNode, (string Kind, string Payload)> describe = null)
    {
        if (canvas == null) return null;

        // The nodes FIRST and numbered, because a wire can only be written once both its ends have names.
        var ids = new Dictionary<ElementItem, int>();
        var nodes = new List<(int Id, ElementItem Item, CanvasNode Node)>();
        var wires = new List<ConnectionItem>();
        var frames = new List<CanvasFrameItem>();

        foreach (var item in canvas.ItemsHere())
        {
            if (!IsGraphs(item)) continue;

            switch (item)
            {
                case ElementItem element when element.Element is CanvasNode node:
                    ids[element] = nodes.Count;
                    nodes.Add((nodes.Count, element, node));
                    break;

                case ConnectionItem wire:
                    wires.Add(wire);
                    break;

                case CanvasFrameItem frame:
                    frames.Add(frame);
                    break;
            }
        }

        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version);

            WriteCamera(writer, canvas);

            writer.WriteStartArray("nodes");
            foreach (var (id, item, node) in nodes) WriteNode(writer, id, item, node, describe);
            writer.WriteEndArray();

            writer.WriteStartArray("links");
            foreach (var wire in wires) WriteLink(writer, wire, ids);
            writer.WriteEndArray();

            writer.WriteStartArray("frames");
            foreach (var frame in frames) WriteFrame(writer, frame);
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>Loads a graph (nodes, wires, comment frames, camera) onto the canvas, replacing the previous graph as
    /// one step of the history; false, with nothing changed, for text this version cannot read. What the file does not
    /// hold - a control put on the plane - stays.</summary>
    /// <param name="make">Asked for each node, with the kind and payload the file recorded. Returning null - or not
    /// being given at all - leaves the engine to make a plain node from the shape.</param>
    public static bool Load(NodeGraph canvas, string text, Func<CanvasNodeSeed, CanvasNode> make = null)
    {
        if (canvas?.Scene is not { } scene || string.IsNullOrWhiteSpace(text)) return false;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            if (!Whole(root, "version", out var written) || written > Version) return false;

            // EVERYTHING READ BEFORE ANYTHING IS CHANGED: a file that turns out to be broken half way through must
            // leave the canvas as it found it, not half a graph.
            var seeds = ReadNodes(root);
            var links = ReadLinks(root);
            var frames = ReadFrames(root, canvas);

            void Replace()
            {
                foreach (var item in new List<ICanvasItem>(canvas.ItemsHere()))
                {
                    if (IsGraphs(item)) scene.Remove(item);
                }

                // The frames FIRST: they stand behind the nodes they are drawn round.
                foreach (var frame in frames) scene.Add(frame);

                var placed = new List<ElementItem>(seeds.Count);
                foreach (var seed in seeds)
                {
                    var node = make?.Invoke(seed) ?? Plain(seed);
                    Dress(node, seed);

                    var element = new ElementItem(node, seed.World);
                    scene.Add(element);
                    placed.Add(element);
                }

                foreach (var (from, fromPin, to, toPin) in links)
                {
                    if (from < 0 || from >= placed.Count || to < 0 || to >= placed.Count) continue;

                    var wire = Join(placed[from], fromPin, placed[to], toPin);
                    if (wire != null) scene.Add(wire);
                }
            }

            if (canvas.History is { } history) history.Record(scene, "Open", Replace);
            else Replace();

            ReadCamera(root, canvas);
            scene.Touch();
        }

        return true;
    }

    // What a graph file holds - and so what loading one replaces.
    private static bool IsGraphs(ICanvasItem item) =>
        item is ElementItem { Element: CanvasNode } or ConnectionItem or CanvasFrameItem;

    private static void WriteCamera(Utf8JsonWriter writer, InfiniteCanvas canvas)
    {
        writer.WriteStartObject("camera");
        writer.WriteNumber("x", canvas.Offset.X);
        writer.WriteNumber("y", canvas.Offset.Y);
        writer.WriteNumber("scale", canvas.Scale);
        writer.WriteEndObject();
    }

    private static void ReadCamera(JsonElement root, InfiniteCanvas canvas)
    {
        if (!root.TryGetProperty("camera", out var camera) || camera.ValueKind != JsonValueKind.Object) return;

        // The SCALE first: the offset is what the camera is pointed at, and setting it at the old zoom would put the
        // graph somewhere else and then zoom about that.
        if (Real(camera, "scale", out var zoom) && zoom > 0)
            canvas.SetCurrentValue(InfiniteCanvas.ScaleProperty, zoom);

        if (Real(camera, "x", out var left) && Real(camera, "y", out var top))
        {
            canvas.SetCurrentValue(InfiniteCanvas.OffsetProperty, new Vector2<double>(left, top));
        }
    }

    private static void WriteNode(Utf8JsonWriter writer, int id, ElementItem item, CanvasNode node,
        Func<CanvasNode, (string Kind, string Payload)> describe)
    {
        // The NODE's own kind unless the application says otherwise. It carries one, so a graph round-trips its kinds
        // with no callback at all, and describe is left for the one thing only the application knows: the state.
        var said = describe?.Invoke(node) ?? (node.Kind, string.Empty);

        writer.WriteStartObject();
        writer.WriteNumber("id", id);
        if (!string.IsNullOrEmpty(said.Kind)) writer.WriteString("kind", said.Kind);
        if (!string.IsNullOrEmpty(said.Payload)) writer.WriteString("payload", said.Payload);
        writer.WriteString("title", node.Title?.ToString() ?? string.Empty);

        writer.WriteStartObject("world");
        writer.WriteNumber("x", item.World.X);
        writer.WriteNumber("y", item.World.Y);
        writer.WriteNumber("width", item.World.Width);
        writer.WriteNumber("height", item.World.Height);
        writer.WriteEndObject();

        WriteSockets(writer, "in", node.InputPins);
        WriteSockets(writer, "out", node.OutputPins);

        writer.WriteEndObject();
    }

    private static void WriteSockets(Utf8JsonWriter writer, string name, IEnumerable<CanvasNodePin> pins)
    {
        writer.WriteStartArray(name);
        foreach (var pin in pins)
        {
            writer.WriteStartObject();
            writer.WriteString("name", pin.Name ?? string.Empty);

            var color = Hex(pin.Color);
            if (!string.IsNullOrEmpty(color)) writer.WriteString("color", color);

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteLink(Utf8JsonWriter writer, ConnectionItem wire, Dictionary<ElementItem, int> ids)
    {
        if (wire.FromItem == null || wire.ToItem == null) return;
        if (!ids.TryGetValue(wire.FromItem, out var from) || !ids.TryGetValue(wire.ToItem, out var to)) return;

        writer.WriteStartObject();
        writer.WriteNumber("from", from);
        writer.WriteString("fromSocket", wire.FromPin?.Name ?? string.Empty);
        writer.WriteNumber("to", to);
        writer.WriteString("toSocket", wire.ToPin?.Name ?? string.Empty);
        writer.WriteEndObject();
    }

    private static void WriteFrame(Utf8JsonWriter writer, CanvasFrameItem frame)
    {
        writer.WriteStartObject();
        writer.WriteString("title", frame.Title ?? string.Empty);

        writer.WriteStartObject("world");
        writer.WriteNumber("x", frame.World.X);
        writer.WriteNumber("y", frame.World.Y);
        writer.WriteNumber("width", frame.World.Width);
        writer.WriteNumber("height", frame.World.Height);
        writer.WriteEndObject();

        // NO OUTLINE is said as null, so it does not come back in the accent the way a missing color does.
        var stroke = Hex(frame.Stroke);
        if (frame.Stroke == null) writer.WriteNull("stroke");
        else if (!string.IsNullOrEmpty(stroke)) writer.WriteString("stroke", stroke);

        var fill = Hex(frame.Fill);
        if (!string.IsNullOrEmpty(fill)) writer.WriteString("fill", fill);

        writer.WriteNumber("thickness", frame.Thickness);
        writer.WriteNumber("titleHeight", frame.TitleHeight);
        writer.WriteEndObject();
    }

    // A frame the file gives no color is drawn in the canvas's accent, as one drawn round a selection is.
    private static List<CanvasFrameItem> ReadFrames(JsonElement root, InfiniteCanvas canvas)
    {
        var frames = new List<CanvasFrameItem>();
        if (!root.TryGetProperty("frames", out var array) || array.ValueKind != JsonValueKind.Array) return frames;

        foreach (var entry in array.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;

            var world = Box(entry);
            if (world.Width <= 0 || world.Height <= 0) continue;

            var outlined = !(entry.TryGetProperty("stroke", out var said) && said.ValueKind == JsonValueKind.Null);
            var stroke = !outlined
                ? null
                : Color(Text(entry, "stroke")) is { } line
                    ? new SolidColorBrush(line)
                    : canvas.SelectionBrush?.Copy();
            var fill = Color(Text(entry, "fill")) is { } wash ? new SolidColorBrush(wash) : null;

            // Kept to what a frame can draw: no outline thinner than nothing, no title strip taller than the frame.
            var frame = new CanvasFrameItem(world, Text(entry, "title"), stroke, fill);
            frame.Thickness = Math.Max(0, Number(entry, "thickness", frame.Thickness));
            frame.TitleHeight = Math.Clamp(Number(entry, "titleHeight", frame.TitleHeight), 0, world.Height);
            frames.Add(frame);
        }

        return frames;
    }

    private static List<CanvasNodeSeed> ReadNodes(JsonElement root)
    {
        var seeds = new List<CanvasNodeSeed>();
        if (!root.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array) return seeds;

        foreach (var node in nodes.EnumerateArray())
        {
            if (node.ValueKind != JsonValueKind.Object) continue;

            seeds.Add(new CanvasNodeSeed
            {
                Kind = Text(node, "kind"),
                Payload = Text(node, "payload"),
                Title = Text(node, "title"),
                World = Box(node),
                Inputs = Sockets(node, "in"),
                Outputs = Sockets(node, "out")
            });
        }

        return seeds;
    }

    private static List<(int From, string FromPin, int To, string ToPin)> ReadLinks(JsonElement root)
    {
        var links = new List<(int, string, int, string)>();
        if (!root.TryGetProperty("links", out var array) || array.ValueKind != JsonValueKind.Array) return links;

        foreach (var link in array.EnumerateArray())
        {
            if (link.ValueKind != JsonValueKind.Object) continue;
            if (!Whole(link, "from", out var fromId) || !Whole(link, "to", out var toId)) continue;

            links.Add((fromId, Text(link, "fromSocket"), toId, Text(link, "toSocket")));
        }

        return links;
    }

    private static List<CanvasSocketSeed> Sockets(JsonElement node, string name)
    {
        var sockets = new List<CanvasSocketSeed>();
        if (!node.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array) return sockets;

        foreach (var socket in array.EnumerateArray())
        {
            if (socket.ValueKind != JsonValueKind.Object) continue;

            sockets.Add(new CanvasSocketSeed { Name = Text(socket, "name"), Color = Text(socket, "color") });
        }

        return sockets;
    }

    private static CanvasNode Plain(CanvasNodeSeed seed) => new();

    // The shape the file recorded, put onto whatever node came back - the application's or the engine's. Sockets it
    // already has KEEP their names and colors only where the file had nothing to say, so a node the application built
    // with its own sockets is not flattened by being loaded.
    private static void Dress(CanvasNode node, CanvasNodeSeed seed)
    {
        // The KIND the file recorded, put back whatever made the node - so a graph opened by an application that did
        // not build this node still knows what it was, and saves it again as what it was.
        if (!string.IsNullOrEmpty(seed.Kind)) node.Kind = seed.Kind;

        if (!string.IsNullOrEmpty(seed.Title)) node.Title = seed.Title;

        Fit(node.InputPins, seed.Inputs, true);
        Fit(node.OutputPins, seed.Outputs, false);
    }

    private static void Fit(System.Collections.ObjectModel.ObservableCollection<CanvasNodePin> pins,
        IReadOnlyList<CanvasSocketSeed> seeds, bool input)
    {
        if (seeds.Count == 0) return;

        while (pins.Count > seeds.Count) pins.RemoveAt(pins.Count - 1);
        while (pins.Count < seeds.Count) pins.Add(new CanvasNodePin { IsInput = input });

        for (var i = 0; i < seeds.Count; i++)
        {
            pins[i].IsInput = input;
            if (!string.IsNullOrEmpty(seeds[i].Name)) pins[i].Name = seeds[i].Name;

            if (Color(seeds[i].Color) is { } color) pins[i].Color = new SolidColorBrush(color);
        }
    }

    // A wire between two sockets found BY NAME. Nothing is invented: a socket the file names and the node does not have
    // means a wire that is simply not made, the same way a saved pane with no control is dropped rather than conjured.
    private static ConnectionItem Join(ElementItem from, string fromPin, ElementItem to, string toPin)
    {
        if (from.Element is not CanvasNode source || to.Element is not CanvasNode target) return null;

        var leaves = Find(source.OutputPins, fromPin);
        var arrives = Find(target.InputPins, toPin);
        if (leaves == null || arrives == null) return null;

        leaves.IsConnected = true;
        arrives.IsConnected = true;

        return new ConnectionItem(from, leaves, to, arrives);
    }

    private static CanvasNodePin Find(IEnumerable<CanvasNodePin> pins, string name)
    {
        foreach (var pin in pins)
        {
            if (string.Equals(pin.Name, name, StringComparison.Ordinal)) return pin;
        }

        return null;
    }

    private static string Text(JsonElement owner, string name) =>
        owner.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static Rect Box(JsonElement node)
    {
        if (!node.TryGetProperty("world", out var world) || world.ValueKind != JsonValueKind.Object) return default;

        return new Rect(Number(world, "x"), Number(world, "y"), Number(world, "width"), Number(world, "height"));
    }

    private static double Number(JsonElement owner, string name, double missing = 0) =>
        Real(owner, name, out var number) ? number : missing;

    // NUMBERS ONLY: asked for a number, a string or a fraction where a whole number belongs throws rather than says no,
    // and a file is not trusted to be well-formed.
    private static bool Real(JsonElement owner, string name, out double value)
    {
        value = 0;
        return owner.TryGetProperty(name, out var found) && found.ValueKind == JsonValueKind.Number &&
               found.TryGetDouble(out value);
    }

    private static bool Whole(JsonElement owner, string name, out int value)
    {
        value = 0;
        return owner.TryGetProperty(name, out var found) && found.ValueKind == JsonValueKind.Number &&
               found.TryGetInt32(out value);
    }

    // INVARIANT hex, because a color written under one language must read back under another - see the note on
    // markup numbers.
    private static string Hex(Brush brush) =>
        brush is SolidColorBrush solid
            ? "#" + solid.Color.A.ToString("X2", CultureInfo.InvariantCulture)
                  + solid.Color.R.ToString("X2", CultureInfo.InvariantCulture)
                  + solid.Color.G.ToString("X2", CultureInfo.InvariantCulture)
                  + solid.Color.B.ToString("X2", CultureInfo.InvariantCulture)
            : string.Empty;

    private static Color? Color(string hex)
    {
        if (string.IsNullOrEmpty(hex) || hex[0] != '#' || hex.Length != 9) return null;

        return byte.TryParse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var a) &&
               byte.TryParse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) &&
               byte.TryParse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) &&
               byte.TryParse(hex.AsSpan(7, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b)
            ? new Color(r, g, b, a)
            : null;
    }
}
