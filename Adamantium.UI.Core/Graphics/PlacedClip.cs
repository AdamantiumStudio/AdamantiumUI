using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;

namespace Adamantium.UI.Core.Graphics;

/// <summary>A rectangle, its corners maybe rounded, that cuts what an element draws and moves with a
/// <see cref="Placement"/>: rewriting the placement's matrix moves the cut without drawing anything again. Made by
/// <see cref="IDrawingSession.PushClip"/>; one inside another cuts by both.</summary>
public sealed class PlacedClip
{
    internal PlacedClip(Rect rect, CornerRadius corners, Placement placement, PlacedClip outer, int ordinal)
    {
        Rect = rect;
        Corners = corners;
        Placement = placement;
        Outer = outer;
        Ordinal = ordinal;
        RecordedMatrix = placement.Matrix;
        RecordedVersion = placement.Version;
    }

    /// <summary>The placement's matrix when the clip was recorded, and its version then: what a renderer that has heard
    /// of no move yet shows.</summary>
    internal Matrix4x4F RecordedMatrix { get; }

    /// <inheritdoc cref="RecordedMatrix"/>
    internal long RecordedVersion { get; }

    /// <summary>The rectangle, in the placement's units.</summary>
    public Rect Rect { get; }

    /// <summary>Its corner radii, in the placement's units.</summary>
    public CornerRadius Corners { get; }

    /// <summary>What the rectangle moves with.</summary>
    public Placement Placement { get; }

    /// <summary>The clip this one was pushed inside, or null.</summary>
    public PlacedClip Outer { get; }

    /// <summary>Which of its element's clips this is, in the order they were pushed: what keeps a clip the same one when
    /// the element draws again.</summary>
    internal int Ordinal { get; }
}
