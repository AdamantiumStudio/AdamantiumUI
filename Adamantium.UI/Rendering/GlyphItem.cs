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

    /// <summary>.x = transform-table slot; .y = atlas layer; .z = depth; .w = opacity slot, whose alpha the glyph shader
    /// multiplies in - so the color carries only the element's own SelfOpacity, never the chain.</summary>
    public Vector4F Params;

    /// <summary>.x = the clip slot this glyph is cut by, or -1 (0 is a valid slot); .yzw = the glyph's synthesized bold
    /// and italic, as <see cref="Adamantium.Graphics.Fonts.FontItem.Synthesis"/> holds them in its xyz.</summary>
    public Vector4F Clip;

    /// <summary>Straight (non-premultiplied) RGBA, element/brush opacity already folded into .w.</summary>
    public Vector4F Color;

    /// <summary>.x = a 'COLR' version 1 layer's paint record plus one, or 0, as
    /// <see cref="Adamantium.Graphics.Fonts.FontItem.Paint"/> holds it (<see cref="LocalRect"/> is then the pen and
    /// baseline, and the pixels per font unit); .y = the element's opacity raised to 2.2, which the layer's own colors
    /// take; .z = the width of a shadow's or a glow's soft edge, as FontItem.Synthesis.w holds it; .w = the share of a
    /// reflection's quad it fades over, as FontItem.Paint.z holds it.</summary>
    public Vector4F Paint;

    /// <summary>A glyph of a font on the way between two key instances, as
    /// <see cref="Adamantium.Graphics.Fonts.FontItem.SecondSource"/> holds it: the second key's cell over the same
    /// quad; zero for a glyph drawn from one field.</summary>
    public Vector4F SecondSource;

    /// <summary>.x = the atlas layer of <see cref="SecondSource"/>; .y = how far the glyph is from the first key to the
    /// second, as <see cref="Adamantium.Graphics.Fonts.FontItem.Second"/> holds them; .z = the glyph's turn in vertical
    /// text, radians clockwise about <see cref="LocalRect"/>'s corner, as
    /// <see cref="Adamantium.Graphics.Fonts.FontItem.Rotation"/> holds it; .w = how strong a reflection starts, as
    /// FontItem.Paint.w holds it.</summary>
    public Vector4F Second;

    /// <summary>The glyph's cell in the layout's units, as <see cref="Adamantium.Graphics.Fonts.FontItem.Cell"/> holds
    /// it: what its effects turn it about.</summary>
    public Vector4F Cell;

    /// <summary>The layout's units to node-local, where the effects are undone to and redone from: x * X + Z,
    /// y * Y + W.</summary>
    public Vector4F ToNode;

    /// <summary>.xy = <see cref="Adamantium.Graphics.Fonts.FontItem.Place"/>; .z = the slot of the block's chain of glyph
    /// effects in the transform table, -1 for none; .w = the element's opacity, which an effect's color takes.</summary>
    public Vector4F Place;
}
