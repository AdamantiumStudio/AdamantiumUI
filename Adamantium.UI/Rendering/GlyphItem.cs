using System.Runtime.InteropServices;
using Adamantium.Mathematics;

namespace Adamantium.UI.Rendering;

/// <summary>
/// One glyph batch instance (FontEffect.fx, pass RenderMsdfBatchInstanced) in node-local space, transformed by its
/// transform-table slot; all-<see cref="Vector4F"/> to match <c>GlyphData</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct GlyphItem
{
    /// <summary>Node-local bounds: x, y, w, h (world-space for a slot-0 identity bake).</summary>
    public Vector4F LocalRect;

    /// <summary>Atlas UV rect: u, v, w, h.</summary>
    public Vector4F Source;

    /// <summary>.x = transform-table slot; .y = atlas layer; .z = depth; .w = opacity slot, sent but not yet read (text
    /// still folds the opacity chain into its color).</summary>
    public Vector4F Params;

    /// <summary>.x = the clip slot this glyph is cut by, or -1 (0 is a valid slot); .yzw spare.</summary>
    public Vector4F Clip;

    /// <summary>Straight (non-premultiplied) RGBA, element/brush opacity already folded into .w.</summary>
    public Vector4F Color;
}
