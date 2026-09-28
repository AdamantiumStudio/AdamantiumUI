using System.Runtime.InteropServices;
using Adamantium.Mathematics;

namespace Adamantium.UI.Rendering;

/// <summary>
/// One gradient rounded-rect instance (BatchEffect.fx, pass GradientRect), up to <see cref="MaxStops"/> stops, matching
/// <c>GradientRectData</c>; stroke fields mirror <see cref="RectItem"/>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct GradientRectItem
{
    /// <summary>Max colour stops packed inline (covers essentially all real gradients). Keep in lock-step with the shader.</summary>
    public const int MaxStops = 8;

    /// <summary>World-space bounds: x, y, w, h.</summary>
    public Vector4F Bounds;

    /// <summary>.x = the LARGEST corner radius; .y = type (1 linear, 2 radial, 3 conic); .z = stop count; .w = spread (0 pad, 1 reflect, 2 repeat).</summary>
    public Vector4F Params;

    /// <summary>The four corner radii: x = top-left, y = top-right, z = bottom-right, w = bottom-left.</summary>
    public Vector4F Radii;

    /// <summary>LOCAL (0..1) gradient geometry. Linear: (startX, startY, endX, endY). Radial: (centerX, centerY, radiusX, radiusY).
    /// Conic: (centerX, centerY, startAngleTurns, _).</summary>
    public Vector4F Geom0;

    /// <summary>Radial only: (originX, originY, _, _) - the focal point. Unused for linear.</summary>
    public Vector4F Geom1;

    /// <summary>Straight stroke colour (opacity folded into the alpha); alpha 0 = no stroke. Four BYTES, read by the
    /// shader as a <c>uint8_t4</c>.</summary>
    public Color StrokeColor;

    /// <summary>Stroke geometry: x = width px, y = align (-1/0/+1), z = dash ON, w = dash GAP.</summary>
    public Vector4F Stroke0;

    /// <summary>Stroke arc-length: x = dash offset, y = trim start, z = trim end, w = flags (join/cap codes).</summary>
    public Vector4F Stroke1;

    /// <summary>Dash runs 2..5 in device px - runs 0 and 1 ride in <see cref="Stroke0"/>.zw and the RUN COUNT is
    /// packed into <see cref="Stroke1"/>.w. A pattern longer than one ON/GAP period lives here.</summary>
    public Vector4F Dash;

    /// <summary>Straight (non-premultiplied) stop colours, opacity folded into the alpha. Only the first .z (stop count)
    /// are valid. Four BYTES each: eight stops at a float4 apiece was 128 bytes of the record, and a stop is a colour -
    /// the same four bytes every other fill in this file carries.</summary>
    public Color Stop0, Stop1, Stop2, Stop3, Stop4, Stop5, Stop6, Stop7;

    /// <summary>Stop offsets (0..1) for stops 0..3.</summary>
    public Vector4F Offsets0;

    /// <summary>Stop offsets (0..1) for stops 4..7.</summary>
    public Vector4F Offsets1;

    /// <summary>.x = the CLIP SLOT this instance is cut by, or -1; .yzw spare. Its own field: every number in
    /// <see cref="Params"/> and <see cref="Geom1"/> is already carrying something.</summary>
    public Vector4F Clip;
}
