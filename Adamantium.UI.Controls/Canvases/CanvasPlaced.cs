using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Graphics;

namespace Adamantium.UI.Controls.Canvases;

/// <summary>The placements one thing on the plane draws with: its main one and the parts it asked for, each kept on the
/// camera by <see cref="CanvasPlacements"/>.</summary>
internal sealed class CanvasPlaced
{
    public readonly Placement Main = new();

    public readonly List<CanvasPlacedPart> Parts = new();

    public Vector2<double> Origin;

    public CanvasTransform Turn;

    public Vector2<double> Pivot;

    /// <summary>How many of <see cref="Parts"/> the record in progress has used.</summary>
    public int Used;
}
