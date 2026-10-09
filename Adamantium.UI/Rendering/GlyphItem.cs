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

    /// <summary>.x = the clip slot this glyph is cut by, or -1 (0 is a valid slot); .yzw = the glyph's synthesized bold
    /// and italic, as <see cref="Adamantium.Graphics.Fonts.FontItem.Synthesis"/> holds them in its xyz.</summary>
    public Vector4F Clip;

    /// <summary>Straight (non-premultiplied) RGBA, element/brush opacity already folded into .w.</summary>
    public Vector4F Color;

    /// <summary>.x = a 'COLR' version 1 layer's paint record plus one, or 0, as
    /// <see cref="Adamantium.Graphics.Fonts.FontItem.Paint"/> holds it (<see cref="LocalRect"/> is then the pen and
    /// baseline, and the pixels per font unit); .y = the element's opacity raised to 2.2, which the layer's own colors
    /// take.</summary>
    public Vector4F Paint;

    /// <summary>A glyph of a font on the way between two key instances, as
    /// <see cref="Adamantium.Graphics.Fonts.FontItem.SecondSource"/> holds it: the second key's cell over the same
    /// quad; zero for a glyph drawn from one field.</summary>
    public Vector4F SecondSource;

    /// <summary>.x = the atlas layer of <see cref="SecondSource"/>; .y = how far the glyph is from the first key to the
    /// second, as <see cref="Adamantium.Graphics.Fonts.FontItem.Second"/> holds them.</summary>
    public Vector4F Second;
}
