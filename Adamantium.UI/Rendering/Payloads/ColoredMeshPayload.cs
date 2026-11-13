using System;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Curves;

namespace Adamantium.UI.Rendering.Payloads;

/// <summary>Triangles with a color at each corner, placed by a matrix on top of the element's world.</summary>
public sealed class ColoredMeshPayload(ColoredMesh mesh, Matrix4x4F localTransform) :
    IEquatable<ColoredMeshPayload>, IRenderCachePolicy, IPlacedPayload
{
    public ColoredMesh Mesh { get; } = mesh;

    /// <summary>The mesh's outline as curves, its fill tiles made on the record thread; null for none.</summary>
    internal PathCurves Outline { get; } = Prepared(mesh?.Outline);

    /// <summary>What the outline held when this was recorded: a geometry reopened is a different outline.</summary>
    public int OutlineVersion { get; } = mesh?.Outline?.Version ?? 0;

    public Matrix4x4F LocalTransform { get; } = localTransform;

    public Placement Placement { get; private set; }

    public long PlacedVersion { get; private set; }

    void IPlacedPayload.PlaceBy(Placement placement)
    {
        Placement = placement;
        PlacedVersion = placement.Version;
    }

    /// <summary>A new mesh or outline is new buffers to upload; a new matrix only moves them.</summary>
    public bool RequiresBufferRebuild(IRenderCachePolicy newState) =>
        newState is not ColoredMeshPayload other || !ReferenceEquals(Mesh, other.Mesh) ||
        OutlineVersion != other.OutlineVersion;

    public bool Equals(ColoredMeshPayload other) =>
        other is not null && ReferenceEquals(Mesh, other.Mesh) && OutlineVersion == other.OutlineVersion &&
        ReferenceEquals(Placement, other.Placement) && (Placement != null || LocalTransform == other.LocalTransform);

    public override bool Equals(object obj) => obj is ColoredMeshPayload other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Mesh, OutlineVersion, Placement != null ? Placement.GetHashCode() : LocalTransform.GetHashCode());

    private static PathCurves Prepared(Geometry outline)
    {
        var curves = outline?.CurvesForDrawing;
        _ = curves?.FillTiles;
        return curves;
    }
}
