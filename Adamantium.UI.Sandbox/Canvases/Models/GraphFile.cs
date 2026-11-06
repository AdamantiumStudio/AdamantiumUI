using System;
using System.Collections.Generic;
using System.Text.Json;
using Adamantium.Core.Collections;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Sandbox.Canvases.ViewModels;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Canvases;

namespace Adamantium.UI.Sandbox.Canvases.Models;

/// <summary>The graph as TEXT, written and read by the page that owns it.
/// <para>The engine has no part in this and should not: a graph is the application's own objects, and only the
/// application knows what they are made of. Nodes are numbered so a wire can name its two ends; what a node carries is
/// written under its kind, which is also what says how to read it back.</para></summary>
public static class GraphFile
{
    private const int Version = 4;
    private const int WithoutFrames = 3;

    private sealed class Saved
    {
        public int Version { get; set; }

        /// <summary>WHICH CATALOG this graph was made with. A file read back with somebody else's kinds finds nothing
        /// by those words and comes back as a plane of blank nodes, which reads as a broken file rather than as the
        /// wrong one - so the set is written down and checked.</summary>
        public string Set { get; set; } = string.Empty;

        public List<SavedNode> Nodes { get; set; } = new();
        public List<SavedWire> Wires { get; set; } = new();
        public List<SavedFrame> Frames { get; set; } = new();
    }

    private sealed class SavedFrame
    {
        public string Title { get; set; } = string.Empty;
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool Outlined { get; set; } = true;
        public int[] Stroke { get; set; }
        public int[] Fill { get; set; }
        public double? Thickness { get; set; }
        public double? TitleHeight { get; set; }
    }

    private sealed class SavedNode
    {
        public string Kind { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public bool Collapsed { get; set; }

        /// <summary>The color a person gave it, as R, G, B, A - empty when it wears the theme's.</summary>
        public int[] Accent { get; set; }
        public List<SavedSocket> In { get; set; } = new();
        public List<SavedSocket> Out { get; set; } = new();
        public string State { get; set; } = string.Empty;
    }

    private sealed class SavedSocket
    {
        public string Name { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;

        /// <summary>How many wires it takes - a person can change it per socket, so it is theirs and gets written down.
        /// </summary>
        public int Takes { get; set; } = 1;
    }

    private sealed class SavedWire
    {
        public int From { get; set; }
        public string FromSocket { get; set; } = string.Empty;
        public int To { get; set; }
        public string ToSocket { get; set; } = string.Empty;
    }

    /// <summary>Writes the graph out. The nodes FIRST and numbered, because a wire can only be written once both its
    /// ends have names. The wires are found by walking the outputs - each one is met exactly once, from the end it
    /// leaves. The comment frames are the scene's, written in paint order.</summary>
    public static string Write(IReadOnlyList<ICanvasNode> nodes, CanvasScene scene, GraphNodeSet set)
    {
        var saved = new Saved { Version = Version, Set = set?.Name ?? string.Empty };
        var numbers = new Dictionary<ICanvasNode, int>();

        foreach (var node in nodes)
        {
            numbers[node] = saved.Nodes.Count;
            saved.Nodes.Add(Describe(node));
        }

        foreach (var node in nodes)
        {
            foreach (var socket in node.Outputs)
            {
                foreach (var wire in socket.Connections)
                {
                    if (wire.ToNode == null || !numbers.TryGetValue(node, out var from)) continue;
                    if (!numbers.TryGetValue(wire.ToNode, out var to)) continue;

                    saved.Wires.Add(new SavedWire
                    {
                        From = from,
                        FromSocket = socket.Name ?? string.Empty,
                        To = to,
                        ToSocket = wire.To?.Name ?? string.Empty
                    });
                }
            }
        }

        if (scene != null)
        {
            foreach (var item in scene.Items)
            {
                if (item is CanvasFrameItem frame) saved.Frames.Add(Describe(frame));
            }
        }

        return JsonSerializer.Serialize(saved, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Reads one back with the given catalog, replacing what is there - the nodes, and the comment frames in the
    /// scene; a frame written in the accent comes back in <paramref name="accent"/>. Says what stopped it and changes
    /// nothing when it cannot: text this version does not understand, or a graph made with another set of kinds.
    /// </summary>
    public static string Read(string text, TrackingCollection<ICanvasNode> nodes, CanvasScene scene, GraphNodeSet set,
        Brush accent)
    {
        Saved saved;
        try
        {
            saved = JsonSerializer.Deserialize<Saved>(text);
        }
        catch (JsonException)
        {
            return "that file is not a graph this version can read";
        }

        if (saved == null || (saved.Version != Version && saved.Version != WithoutFrames))
        {
            return "that file is not a graph this version can read";
        }

        // THE WRONG CATALOG is not a broken file, and saying so is the difference between "this needs the shading
        // nodes" and a plane of nodes that are blank for no visible reason.
        if (!string.IsNullOrEmpty(saved.Set) && set != null && saved.Set != set.Name)
        {
            return $"that graph is made of {saved.Set} nodes, and this page is holding {set.Name}";
        }

        // Everything is checked BEFORE anything is let go of, so a broken entry leaves the graph as it was.
        if (!Whole(saved)) return "that file is not a graph this version can read";

        nodes.Clear();

        var made = new List<ICanvasNode>(saved.Nodes.Count);

        foreach (var written in saved.Nodes)
        {
            var node = new CanvasNodeViewModel
            {
                Kind = written.Kind,
                Title = written.Title,
                Left = written.Left,
                Top = written.Top,
                Width = written.Width,
                IsCollapsed = written.Collapsed,
                // The file's own color, and the kind's when the file has none - a graph written before colors existed
                // comes back looking like a new one rather than gray.
                Accent = Paint(written.Accent) ?? set?.Named(written.Kind)?.Accent,

                // The catalog makes the inside, the same as a pick from the palette does, and the file's own state is
                // read into it. Sockets come with it and are then replaced by the ones written down, which carry names
                // a person may have changed.
                Specialization = State(written, set)
            };

            node.Inputs.Clear();
            node.Outputs.Clear();

            foreach (var socket in written.In)
            {
                node.Inputs.Add(new CanvasSocketViewModel(socket.Takes) { Name = socket.Name, Kind = socket.Kind });
            }

            foreach (var socket in written.Out)
            {
                node.Outputs.Add(new CanvasSocketViewModel(socket.Takes) { Name = socket.Name, Kind = socket.Kind });
            }

            made.Add(node);
            nodes.Add(node);
        }

        foreach (var written in saved.Wires)
        {
            if (written.From < 0 || written.From >= made.Count) continue;
            if (written.To < 0 || written.To >= made.Count) continue;

            if (Named(made[written.From].Outputs, written.FromSocket) is not { } from) continue;
            if (Named(made[written.To].Inputs, written.ToSocket) is not { } to) continue;

            // Made is joined: the wire seats itself in both sockets, and there is nothing to add it to afterwards.
            _ = new CanvasConnection(from, to);
        }

        if (scene != null) Frame(saved.Frames, scene, accent);

        return null;
    }

    private static void Frame(List<SavedFrame> written, CanvasScene scene, Brush accent)
    {
        for (var i = scene.Items.Count - 1; i >= 0; i--)
        {
            if (scene.Items[i] is CanvasFrameItem old) scene.Remove(old);
        }

        var made = new List<CanvasFrameItem>(written.Count);

        foreach (var entry in written)
        {
            if (entry.Width <= 0 || entry.Height <= 0) continue;

            var stroke = !entry.Outlined
                ? null
                : Paint(entry.Stroke) is { } line
                    ? new SolidColorBrush(line)
                    : accent?.Copy();
            var fill = Paint(entry.Fill) is { } wash ? new SolidColorBrush(wash) : null;

            var frame = new CanvasFrameItem(new Rect(entry.Left, entry.Top, entry.Width, entry.Height), entry.Title,
                stroke, fill);
            frame.Thickness = Math.Max(0, entry.Thickness ?? frame.Thickness);
            frame.TitleHeight = Math.Clamp(entry.TitleHeight ?? frame.TitleHeight, 0, entry.Height);

            made.Add(frame);
            scene.Add(frame);
        }

        for (var i = made.Count - 1; i >= 0; i--) scene.SendToBack(made[i]);
    }

    private static bool Whole(Saved saved)
    {
        saved.Frames ??= [];

        if (saved.Nodes == null || saved.Wires == null) return false;

        foreach (var node in saved.Nodes)
        {
            if (node == null || node.In == null || node.Out == null) return false;
            if (!Finite(node.Left, node.Top, node.Width) || !Channels(node.Accent)) return false;

            foreach (var socket in node.In)
            {
                if (socket == null) return false;
            }

            foreach (var socket in node.Out)
            {
                if (socket == null) return false;
            }
        }

        foreach (var wire in saved.Wires)
        {
            if (wire == null) return false;
        }

        foreach (var frame in saved.Frames)
        {
            if (frame == null || !Channels(frame.Stroke) || !Channels(frame.Fill)) return false;
            if (!Finite(frame.Left, frame.Top, frame.Width, frame.Height)) return false;
            if (!Finite(frame.Thickness ?? 0, frame.TitleHeight ?? 0)) return false;
        }

        return true;
    }

    private static bool Finite(params double[] values)
    {
        foreach (var value in values)
        {
            if (!double.IsFinite(value)) return false;
        }

        return true;
    }

    private static bool Channels(int[] written)
    {
        if (written == null) return true;
        if (written.Length != 4) return false;

        foreach (var channel in written)
        {
            if (channel is < byte.MinValue or > byte.MaxValue) return false;
        }

        return true;
    }

    private static SavedFrame Describe(CanvasFrameItem frame) => new()
    {
        Title = frame.Title ?? string.Empty,
        Left = frame.World.X,
        Top = frame.World.Y,
        Width = frame.World.Width,
        Height = frame.World.Height,
        Outlined = frame.Stroke != null,
        Stroke = Rgba(frame.Stroke),
        Fill = Rgba(frame.Fill),
        Thickness = frame.Thickness,
        TitleHeight = frame.TitleHeight
    };

    private static int[] Rgba(Brush brush) =>
        brush is SolidColorBrush { Color: var c } ? [c.R, c.G, c.B, c.A] : null;

    private static SavedNode Describe(ICanvasNode node)
    {
        var written = new SavedNode
        {
            Kind = node.Kind ?? string.Empty,
            Title = node.Title ?? string.Empty,
            Left = node.Left,
            Top = node.Top,
            Width = node.Width,
            Collapsed = node.IsCollapsed,
            Accent = node.Accent is { } paint ? [paint.R, paint.G, paint.B, paint.A] : null,
            State = node.Specialization is { } state
                ? JsonSerializer.Serialize(state, state.GetType(), ColorJson.Options)
                : string.Empty
        };

        foreach (var socket in node.Inputs)
        {
            written.In.Add(new SavedSocket
            {
                Name = socket.Name ?? string.Empty,
                Kind = socket.Kind ?? string.Empty,
                Takes = socket.Capacity
            });
        }

        foreach (var socket in node.Outputs)
        {
            written.Out.Add(new SavedSocket
            {
                Name = socket.Name ?? string.Empty,
                Kind = socket.Kind ?? string.Empty,
                Takes = socket.Capacity
            });
        }

        return written;
    }

    // A state is text, and text can only be read into a TYPE - which the kind is what names. A state that does not fit
    // what this version says that kind is gets left behind: the node comes back fresh rather than half-old.
    private static ICanvasNodeSpecialization State(SavedNode written, GraphNodeSet set)
    {
        if (set?.Named(written.Kind)?.Create() is not NodeSpecialization fresh) return null;
        if (string.IsNullOrEmpty(written.State)) return fresh;

        try
        {
            if (JsonSerializer.Deserialize(written.State, fresh.GetType(), ColorJson.Options) is not NodeSpecialization read)
            {
                return fresh;
            }

            // The SHAPE is this version's, not the file's - the file carries what a person set, and what sockets a Mix
            // has is what the catalog says today.
            read.In = fresh.In;
            read.Out = fresh.Out;
            read.Takes = fresh.Takes;

            return read;
        }
        catch (JsonException)
        {
            return fresh;
        }
    }

    // Null for a node that was never colored - which is what wears the theme's accent.
    private static Adamantium.Mathematics.Color? Paint(int[] written) =>
        written is { Length: 4 }
            ? Adamantium.Mathematics.Color.FromRgba((byte)written[0], (byte)written[1], (byte)written[2],
                (byte)written[3])
            : null;

    private static ICanvasSocket Named(IReadOnlyList<ICanvasSocket> sockets, string name)
    {
        foreach (var socket in sockets)
        {
            if (socket.Name == name) return socket;
        }

        return null;
    }
}
