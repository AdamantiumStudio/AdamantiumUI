using System.Collections.Generic;
using Adamantium.UI.Controls.DrawingBoard;

namespace Adamantium.UI.Sandbox.DrawingBoard.Models;

/// <summary>A named catalog of node kinds, bound to the canvas as its kind list; sections come from
/// <see cref="ICanvasNodeKind.Group"/>. A saved graph records the name to reopen with the same set.</summary>
public sealed class GraphNodeSet
{
    public GraphNodeSet(string name, IReadOnlyList<ICanvasNodeKind> kinds)
    {
        Name = name;
        Kinds = kinds;
    }

    public string Name { get; }

    public IReadOnlyList<ICanvasNodeKind> Kinds { get; }

    /// <summary>The kind by that word, or null when this set has no such kind.</summary>
    public ICanvasNodeKind Named(string kind)
    {
        foreach (var entry in Kinds)
        {
            if (entry.Kind == kind) return entry;
        }

        return null;
    }
}
