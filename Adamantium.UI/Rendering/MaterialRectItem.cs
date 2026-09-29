using System.Runtime.InteropServices;
using Adamantium.Mathematics;

namespace Adamantium.UI.Rendering;

/// <summary>
/// One backdrop material instance (BrushEffect.fx, technique Material), matching <c>MaterialRectData</c>. The source image
/// and <c>SourceRect</c> are bound per segment, so mica can follow window moves at draw time.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct MaterialRectItem
{
    /// <summary>Node-local bounds: x, y, w, h.</summary>
    public Vector4F Bounds;

    /// <summary>.x = corner radius (device px; NEGATIVE flags ellipse / regular polygon, as in the other SDF batches);
    /// .y = transform-table slot; .z = the element's own alpha; .w = opacity slot.
    ///
    /// <para>.z used to carry the material KIND, which nothing read: both the pass and the source are chosen on the CPU,
    /// so by the time a fragment runs there is nothing left to branch on.</para></summary>
    public Vector4F Params;

    /// <summary>The four corner radii: x = TL, y = TR, z = BR, w = BL.</summary>
    public Vector4F Radii;

    /// <summary>The tint laid over the capture, straight RGBA. <c>.w</c> is the tint's STRENGTH, not the element's
    /// alpha: at 0 the material is clear glass, at 1 it is a painted panel.</summary>
    public Vector4F Tint;

    /// <summary>.x = extra blur radius in device px, .y = grain amount, .z = refraction in device px (LiquidGlass only),
    /// .w = 1 when Source is pinned to the ELEMENT (the shader then takes its coordinates from the shape).</summary>
    public Vector4F Knobs;

    /// <summary>The pen, in the same three slots every other SDF batch bakes it into (see RectBatchCollector.BakeStroke),
    /// so the shared CompositeFillStroke draws it: color, then .x width / .y alignment / .zw dash run, then dash offset,
    /// trim and flags. Zero width = no pen.</summary>
    public Vector4F StrokeColor;
    public Vector4F Stroke0;
    public Vector4F Stroke1;

    /// <summary>.x = the ROUNDED CLIP's slot, or -1; .yzw spare. Its own field rather than one of Stroke1's unused
    /// components: those are the pen's dash/trim/flags, unused only because this batch bakes solid pens today.</summary>
    public Vector4F Clip;

    /// <summary>Surfaces only (velvet, metal): .rgb the cloth color or metal's face-on reflectance; .a grain scale in
    /// device px.</summary>
    public Vector4F Surface;

    /// <summary>SURFACES only: .rgb what the surface answers the light with - the grazing sheen for cloth, the studio
    /// environment for metal - and .a its roughness.</summary>
    public Vector4F Response;

    /// <summary>Surfaces only: .x grain direction and .y light angle (radians), .z light elevation (0 grazing, 1 straight
    /// on), .w the wood figure code (sawing and varnish).</summary>
    public Vector4F Light;
}
