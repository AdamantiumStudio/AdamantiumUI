using System.Runtime.InteropServices;
using Adamantium.Mathematics;

namespace Adamantium.UI.Rendering;

/// <summary>
/// One ellipse SDF batch instance (BatchEffect.fx, pass Ellipse), world-space, matching the shader's <c>EllipseData</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct EllipseItem
{
    /// <summary>Node-local bounding box: x, y, w, h (world for slot-0 legacy bakes - the identity matrix).</summary>
    public Vector4F Bounds;

    /// <summary>.x = transform-table slot (0 = identity); .yzw reserved. Mirrors the shader's EllipseData.Params.</summary>
    public Vector4F Params;

    /// <summary>Straight (non-premultiplied) fill color, element/brush opacity already folded into the alpha. Four
    /// BYTES, read by the shader as a <c>uint8_t4</c>.</summary>
    public Color Color;

    /// <summary>Straight stroke color (opacity folded into the alpha); alpha 0 = no stroke.</summary>
    public Color StrokeColor;

    /// <summary>Stroke geometry: x = width in device px, y = alignment (-1 inside, 0 center, +1 outside),
    /// z = dash ON length (device px, 0 = solid), w = dash GAP length (device px).</summary>
    public Vector4F Stroke0;

    /// <summary>Stroke arc-length features: x = dash offset (device px), y = trim start (0..1),
    /// z = trim end (0..1), w = flags (join/cap codes, packed).</summary>
    public Vector4F Stroke1;

    /// <summary>Dash runs 2..5 in device px - runs 0 and 1 ride in <see cref="Stroke0"/>.zw and the RUN COUNT is
    /// packed into <see cref="Stroke1"/>.w. A pattern longer than one ON/GAP period lives here.</summary>
    public Vector4F Dash;

    /// <summary>x/y = start/end in radians of the parametric angle; z = closing (0 none, 1 <c>Sector</c>,
    /// 2 <c>EdgeToEdge</c>); w = inward ring thickness in device px (0 = solid).</summary>
    public Vector4F Arc;
}
