using System;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Rendering.Payloads;

public class GeometryPayload(Brush brush, Geometry geometry, Pen pen = null, Matrix4x4F? localTransform = null) : IEquatable<GeometryPayload>, IRenderCachePolicy
{
    /// <summary>Where this draw places the geometry, ON TOP of the element's own world transform - how a
    /// <see cref="Adamantium.UI.Core.Media.Drawings.Drawing"/> puts many shapes at their own positions and scales while
    /// they all belong to one element. Identity for every ordinary draw. It rides the INSTANCE, never the mesh, so a
    /// shape drawn at five sizes is still one mesh and five instances.</summary>
    public Matrix4x4F LocalTransform { get; } = localTransform ?? Matrix4x4F.Identity;

    // The LIVE brush, read through its immutable snapshot - see RectanglePayload.
    private readonly Brush _brush = brush?.ForRendering();

    public Brush Brush => _brush?.Snapshot;

    /// <summary>The LIVE brush, by reference. What draws per-unit holds THIS and dereferences its snapshot per draw:
    /// holding the snapshot object instead freezes the fill at record time, so dragging a brush's own parameters
    /// changed nothing on screen until something else forced a re-record.</summary>
    internal Brush LiveBrush => _brush;

    public Geometry Geometry { get; } = Tessellate(geometry);

    /// <summary>What the geometry held WHEN THIS PAYLOAD WAS RECORDED. The instance is not enough: a Polygon reopens its
    /// one StreamGeometry with new points, so the same object describes a different shape from one record to the next.</summary>
    public int GeometryVersion { get; } = geometry?.Version ?? 0;

    // Tessellates on the record thread; the render thread must only read the mesh.
    private static Geometry Tessellate(Geometry g)
    {
        g?.ProcessGeometry(GeometryType.Both);
        return g;
    }

    // A copy, since the caller's pen stays editable on the loop thread; its brush stays live and is read via its snapshot.
    public Pen Pen { get; } = pen?.CloneForRendering();

    public bool Equals(GeometryPayload other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Equals(Brush, other.Brush) && Geometry.Equals(other.Geometry)
               && GeometryVersion == other.GeometryVersion && Equals(Pen, other.Pen)
               && LocalTransform == other.LocalTransform;
    }

    public override bool Equals(object obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((GeometryPayload)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Brush, Geometry, GeometryVersion, Pen, LocalTransform);
    }

    public bool RequiresBufferRebuild(IRenderCachePolicy newState)
    {
        if (newState is not GeometryPayload geometryPayload) return true;

        // NOT the instance alone: a Polygon reuses ONE StreamGeometry and reopens it with new points, so the reference
        // is identical while the shape is not. That read as "nothing to rebuild" and the GPU kept the mesh tessellated
        // from the first record - the figure stopped resizing and only its slot moved.
        return Geometry != geometryPayload.Geometry || GeometryVersion != geometryPayload.GeometryVersion;
    }
}