using System.Runtime.InteropServices;
using Adamantium.Mathematics;

namespace Adamantium.UI.Rendering.Retained;

/// <summary>
/// A pattern/noise-filled instance of a shared mesh: world transform, pattern fields and local bounds, evaluated with the
/// same <c>PatternMix</c> as the SDF pattern pass.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct PatternGeometryInstance
{
    /// <summary>Per-instance transform RELATIVE to the transform-table slot in <see cref="Params"/>.w (element local ->
    /// slot space). Row-vector convention (as GeometryInstance). The vertex shader applies the slot matrix on top, so
    /// moving the slot's node never touches this record.</summary>
    public Matrix4x4F Local;

    /// <summary>.y = pattern type (matches PatternRectData: 0 checker..4 simplex/7 perlin/8 value/9 worley/10 ridged/
    /// 11 turbulence/12 voronoi-borders/13 combustible); .z = cell size in LOCAL units; .w = transform-table slot
    /// (same place the SDF pattern keeps it). .x unused.</summary>
    public Vector4F Params;

    /// <summary>The shape's local-space bounds (minX, minY, sizeX, sizeY): the pattern origin is minXY; combustible centers on it.</summary>
    public Vector4F LocalBounds;

    /// <summary>Primary color, straight RGBA, opacity folded. Float4 because the mesh material reuses this record for
    /// values outside 0..1.</summary>
    public Vector4F Color1;

    /// <summary>Secondary color, straight RGBA, opacity folded. Carries the mesh material's response - see
    /// <see cref="Color1"/> for why this one cannot be packed either.</summary>
    public Vector4F Color2;

    /// <summary>Optional MID color for the 3-color noise gradient-map (.w == 0 = off). Also the combustible custom
    /// ramp mid, and the mesh material's light - see <see cref="Color1"/>.</summary>
    public Vector4F Color3;

    /// <summary>Noise params (noise types only): x octaves (sign = animate flag), y seed, z lacunarity, w gain
    /// (or, for combustible, the fire-palette flag).</summary>
    public Vector4F Noise;

    /// <summary>.x = the offset subtracted from the live clock while animating (the brush's own phase); .y = the phase to hold
    /// while NOT animating. .z spare; .w carries the ROUNDED CLIP's slot when this record holds a MATERIAL - so a
    /// material on a mesh has NO free component here, which is why the wood pass cannot be told which cut to draw.
    /// </summary>
    public Vector4F Anim;
}
