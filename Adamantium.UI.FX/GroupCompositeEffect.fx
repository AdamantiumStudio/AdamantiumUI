// A GROUP of draws laid down: what an element drew between PushGroup and PopGroup, drawn apart into a texture, put back
// cut by its masks, faded, and mixed with what lies under it by a blend mode (ISO 32000-2, 11.3.5 and 11.3.6).

#include "Includes/CommonData.fxh"

// Where the group goes, in device pixels from the viewport's corner: .xy its top-left corner, .zw its size.
float4 LayerBox;

// The group's region on the window, in device pixels: where a texel of each of its textures was drawn.
float4 SourceRegion;

// How much of each texture the region took (.xy, 0..1): textures only grow, so a smaller region uses their top-left part.
float4 LayerTexels;

// Up to three masks over the same region - a path, glyphs, a soft mask - multiplied together. Each: .xy its texels, .z 1
// when there is one, .w 1 to read its luminosity, 0 its alpha.
Texture2D Mask0Texture;
Texture2D Mask1Texture;
Texture2D Mask2Texture;
float4 Mask0;
float4 Mask1;
float4 Mask2;

// What lay under the region before the group, copied texel for texel (.xy its texels); read by every mode but Normal.
Texture2D BackdropTexture;
float4 Backdrop;

// The group's opacity.
float GroupAlpha;

// The blend mode, in BlendMode's order: 0 Normal ... 15 Luminosity; 16 the content in place of the backdrop.
float BlendMode;

float Luminance(float3 c) { return dot(c, float3(0.3, 0.59, 0.11)); }

float MaskValue(Texture2D mask, float4 info, float2 inRegion)
{
    if (info.z < 0.5) return 1.0;
    float4 m = mask.SampleLevel(SourceSampler, inRegion * info.xy, 0);
    return info.w > 0.5 ? Luminance(m.rgb) : m.a;
}

float HardLight(float cb, float cs)
{
    return cs <= 0.5 ? cb * 2.0 * cs : cb + (2.0 * cs - 1.0) - cb * (2.0 * cs - 1.0);
}

float SoftLightD(float x)
{
    return x <= 0.25 ? ((16.0 * x - 12.0) * x + 4.0) * x : sqrt(x);
}

float Separable(int mode, float cb, float cs)
{
    switch (mode)
    {
        case 1: return cb * cs;
        case 2: return cb + cs - cb * cs;
        case 3: return HardLight(cs, cb);
        case 4: return min(cb, cs);
        case 5: return max(cb, cs);
        case 6: return cb <= 0.0 ? 0.0 : cs >= 1.0 ? 1.0 : min(1.0, cb / (1.0 - cs));
        case 7: return cb >= 1.0 ? 1.0 : cs <= 0.0 ? 0.0 : 1.0 - min(1.0, (1.0 - cb) / cs);
        case 8: return HardLight(cb, cs);
        case 9: return cs <= 0.5 ? cb - (1.0 - 2.0 * cs) * cb * (1.0 - cb)
                                 : cb + (2.0 * cs - 1.0) * (SoftLightD(cb) - cb);
        case 10: return abs(cb - cs);
        case 11: return cb + cs - 2.0 * cb * cs;
        default: return cs;
    }
}

float3 ClipColor(float3 c)
{
    float l = Luminance(c);
    float n = min(c.r, min(c.g, c.b));
    float x = max(c.r, max(c.g, c.b));
    if (n < 0.0) c = l + (c - l) * l / max(l - n, 1e-6);
    if (x > 1.0) c = l + (c - l) * (1.0 - l) / max(x - l, 1e-6);
    return c;
}

float3 SetLum(float3 c, float l)
{
    return ClipColor(c + (l - Luminance(c)));
}

float Sat(float3 c)
{
    return max(c.r, max(c.g, c.b)) - min(c.r, min(c.g, c.b));
}

// The color with its largest component s, its smallest 0 and its middle one kept in proportion between them.
float3 SetSat(float3 c, float s)
{
    float n = min(c.r, min(c.g, c.b));
    float x = max(c.r, max(c.g, c.b));
    return x > n ? (c - n) * s / (x - n) : float3(0.0, 0.0, 0.0);
}

float3 Blend(int mode, float3 cb, float3 cs)
{
    switch (mode)
    {
        case 12: return SetLum(SetSat(cs, Sat(cb)), Luminance(cb));
        case 13: return SetLum(SetSat(cb, Sat(cs)), Luminance(cb));
        case 14: return SetLum(cs, Luminance(cb));
        case 15: return SetLum(cb, Luminance(cs));
        default: return float3(Separable(mode, cb.r, cs.r), Separable(mode, cb.g, cs.g), Separable(mode, cb.b, cs.b));
    }
}

struct GroupVSOutput
{
    float4 Position : SV_Position;
};

[shader("vertex")]
GroupVSOutput GroupCompositeVS(uint id : SV_VertexID)
{
    GroupVSOutput o;
    float2 corner = float2(id & 1, (id >> 1) & 1);
    float2 pixel = LayerBox.xy + corner * LayerBox.zw;
    o.Position = float4(pixel / max(ViewportSize, float2(1.0, 1.0)) * 2.0 - 1.0, 0.0, 1.0);
    return o;
}

// Normal is laid down with the premultiplied "over"; every other mode writes the finished color with blending off, from
// the copy of what lay under it: c = (1 - as) cb + (1 - ab) cs + as ab B(Cb, Cs), all premultiplied, a = as + ab - as ab.
[shader("fragment")]
float4 GroupCompositePS(GroupVSOutput input) : SV_Target
{
    float2 here = SurfacePixel(input.Position.xy);
    float2 inRegion = (here - SourceRegion.xy) / max(SourceRegion.zw, float2(1.0, 1.0));
    float inside = all(inRegion >= 0.0) && all(inRegion < 1.0) ? 1.0 : 0.0;
    float2 at = saturate(inRegion);
    float shown = GroupAlpha * inside * MaskValue(Mask0Texture, Mask0, at) * MaskValue(Mask1Texture, Mask1, at)
                  * MaskValue(Mask2Texture, Mask2, at);
    float4 drawn = SourceTexture.SampleLevel(SourceSampler, at * LayerTexels.xy, 0);
    float4 source = drawn * shown;
    int mode = int(BlendMode + 0.5);
    if (mode == 0) return source;

    float4 under = BackdropTexture.SampleLevel(SourceSampler, at * Backdrop.xy, 0);
    // A cut whose content started as what lay under it: that content in its place, where the masks let it through.
    if (mode == 16) return lerp(under, drawn, shown);

    float3 cs = source.a > 0.0 ? saturate(source.rgb / source.a) : float3(0.0, 0.0, 0.0);
    float3 cb = under.a > 0.0 ? saturate(under.rgb / under.a) : float3(0.0, 0.0, 0.0);
    float3 mixed = Blend(mode, cb, cs);
    float3 color = (1.0 - source.a) * under.rgb + (1.0 - under.a) * source.rgb + source.a * under.a * mixed;
    return float4(color, source.a + under.a - source.a * under.a);
}

technique GroupComposite
{
    pass Draw
    {
        VertexShader = GroupCompositeVS;
        PixelShader = GroupCompositePS;
    }
}
