using System.Runtime.InteropServices;
using Adamantium.Mathematics;

namespace Adamantium.UI.Rendering;

/// <summary>
/// One regular polygon instance (BatchEffect.fx, pass Polygon) inscribed in its box, matching <c>PolygonData</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct PolygonItem
{
    /// <summary>Node-local bounding box: x, y, w, h (world for slot-0 bakes - the identity matrix).</summary>
    public Vector4F Bounds;

    /// <summary>.x = transform-table slot (0 = identity); .y = CORNERS (3 and up); .z = ring thickness in device px
    /// (0 = solid); .w = start angle in RADIANS - where corner 0 sits, 0 being the +x axis.</summary>
    public Vector4F Params;

    /// <summary>Straight (non-premultiplied) fill colour, element/brush opacity already folded into the alpha. Four
    /// BYTES, read by the shader as a <c>uint8_t4</c>.</summary>
    public Color Color;

    /// <summary>Straight stroke colour (opacity folded into the alpha); alpha 0 = no stroke.</summary>
    public Color StrokeColor;

    /// <summary>Stroke geometry: x = width in device px, y = alignment (-1 inside, 0 center, +1 outside),
    /// z = dash ON length (device px, 0 = solid), w = dash GAP length (device px).</summary>
    public Vector4F Stroke0;

    /// <summary>Stroke arc-length features: x = dash offset (device px), y = trim start (0..1),
    /// z = trim end (0..1), w = flags (join/cap codes, packed).</summary>
    public Vector4F Stroke1;

    /// <summary>Dash runs 2..5 in device px - runs 0 and 1 ride in <see cref="Stroke0"/>.zw.</summary>
    public Vector4F Dash;

    /// <summary>.x = clip slot or -1; .y = opacity slot (-1 = opaque); .zw spare.</summary>
    public Vector4F Clip;
}
