using System.Runtime.InteropServices;
using Adamantium.Mathematics;

namespace Adamantium.UI.Rendering.Retained;

/// <summary>
/// A gradient-filled instance of a shared mesh: world transform, gradient (up to 8 stops) and local bounds for uv
/// mapping. The vertex shader passes the gradient to the pixel shader through flat interpolators.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct GradientGeometryInstance
{
    /// <summary>Per-instance transform RELATIVE to the transform-table slot in <see cref="Geom1"/>.w (element local ->
    /// slot space). Row-vector convention (as GeometryInstance). The vertex shader applies the slot matrix on top, so
    /// moving the slot's node never touches this record.</summary>
    public Matrix4x4F Local;

    /// <summary>.x type (1 linear/2 radial); .y spread (0 pad/1 reflect/2 repeat); .z stop count; .w interp mode (0 sRGB/1 OKLab).</summary>
    public Vector4F Params;

    /// <summary>LOCAL 0..1: linear (startXY, endXY) | radial (centerXY, radiusXY).</summary>
    public Vector4F Geom0;

    /// <summary>Radial focal (originXY, _, _) - unused for linear; .w = the transform-table slot (same place the SDF
    /// gradient keeps it, so the two records read alike).</summary>
    public Vector4F Geom1;

    /// <summary>The shape's local-space bounds (minX, minY, sizeX, sizeY): a fragment's uv = (localPos - min) / size.</summary>
    public Vector4F LocalBounds;

    /// <summary>Straight stop colors (opacity folded into alpha), four bytes each as in <see cref="GradientRectItem"/>;
    /// only the first Params.z are valid.</summary>
    public Color Stop0, Stop1, Stop2, Stop3, Stop4, Stop5, Stop6, Stop7;

    /// <summary>Stop offsets 0..3.</summary>
    public Vector4F Offsets0;

    /// <summary>Stop offsets 4..7.</summary>
    public Vector4F Offsets1;

    /// <summary>.x = the ROUNDED CLIP's slot, or -1; .yzw spare. Its own field: this record already packs the opacity
    /// slot INTO Params.w for want of room, and there is no second spare component to pack into.</summary>
    public Vector4F Clip;
}
