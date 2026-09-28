using System.Collections.Generic;
using Adamantium.Mathematics;

namespace Adamantium.UI.Controls.DrawingBoard;

/// <summary>An item reshaped by its own points (a curve, a polyline) rather than a box; such items usually offer no resize
/// grips (<see cref="ICanvasItem.Handles"/>).</summary>
public interface ICanvasPoints
{
    /// <summary>The points, in WORLD units, in the order they are joined.</summary>
    IReadOnlyList<Vector2> Points { get; }

    /// <summary>Moves one of them. The canvas calls this while a point handle is dragged; anything the item has to
    /// rebuild because of it - a cached curve, a bounding box - it rebuilds here.</summary>
    void MovePoint(int index, Vector2 world);
}
