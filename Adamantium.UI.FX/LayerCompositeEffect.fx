// A LAYER laid down: a subtree drawn at full strength into a texture of its own, put back over the window ONCE with the
// group's opacity - so where its children overlap, the background shows through as much as anywhere else.

#include "Includes/CommonData.fxh"
#include "Includes/GradientMath.fxh"

// Where the layer goes, in device pixels from the viewport's corner: .xy its top-left corner, .zw its size. The texture
// holds exactly that region, so a texel lands on a pixel.
float4 LayerBox;

// How much of the texture the layer took (.xy, 0..1): textures only grow, so a smaller layer uses its top-left part.
float4 LayerTexels;

// The group's opacity with every ancestor's folded in.
float LayerAlpha;

// The opacity mask, laid over the element's own box: 0 none, 1 linear, 2 radial, 3 conic (as in GradientMath), 4 four
// corners. Only the alpha of its stops counts.
float MaskType;
float4 MaskGeom0;
float4 MaskGeom1;

// From a window pixel to the element's box, 0..1, through a projection: with p = (x, y, 1), the box point is
// (dot(MaskU.xyz, p), dot(MaskV.xyz, p)) / dot(MaskW.xyz, p).
float4 MaskU;
float4 MaskV;
float4 MaskW;

// Up to eight stops: their alphas and offsets. MaskParams.x is the count, .y the spread.
float4 MaskAlpha0;
float4 MaskAlpha1;
float4 MaskOffsets0;
float4 MaskOffsets1;
float4 MaskParams;

// Soft edges: how deep inside what the layer drew each texel lies (LayerSoftEdgeEffect), read where SoftOn is 1.
Texture2D SoftTexture;
float SoftOn;

// A soft-edged element's own glow and shadow, drawn apart from what the fade reaches and laid under it, read where
// BandsOn is 1. Same region, same texels.
Texture2D BandsTexture;
float BandsOn;

// The layer's own region on the window, in device pixels: where a texel of it was drawn.
float4 SourceRegion;

// A reflection, in window device pixels: .x its strength, .y the row where the mirrored element begins, .z the row where
// it is gone, .w the floor - the row it shows from.
float4 Mirror;

// .x the rows over which it fades out before Mirror.z; .w what this draw is: 0 the layer alone, 1 the layer over its
// reflection (it leaves what lies below the floor and inside the mirrored layer to the reflection), 2 the reflection.
float4 MirrorFade;

// The mirrored layer on the window: .xy its first and past-last columns, .z its past-last row; .w half its sideways
// offset, in pixels.
float4 MirrorQuad;

float Role() { return MirrorFade.w; }

// Between two stops closer than a pixel, a one-pixel ramp centered on them, as the fills do.
float MaskRamp(float t, float aa)
{
    int n = int(MaskParams.x);
    if (n <= 0) return 0.0;
    float alphas[8];
    alphas[0] = MaskAlpha0.x; alphas[1] = MaskAlpha0.y; alphas[2] = MaskAlpha0.z; alphas[3] = MaskAlpha0.w;
    alphas[4] = MaskAlpha1.x; alphas[5] = MaskAlpha1.y; alphas[6] = MaskAlpha1.z; alphas[7] = MaskAlpha1.w;
    float offsets[8];
    offsets[0] = MaskOffsets0.x; offsets[1] = MaskOffsets0.y; offsets[2] = MaskOffsets0.z; offsets[3] = MaskOffsets0.w;
    offsets[4] = MaskOffsets1.x; offsets[5] = MaskOffsets1.y; offsets[6] = MaskOffsets1.z; offsets[7] = MaskOffsets1.w;
    float alpha = alphas[0];
    for (int i = 1; i < n; i++)
    {
        float lo = offsets[i - 1];
        float hi = offsets[i];
        float span = hi - lo;
        float blend = span > aa
            ? saturate((t - lo) / max(span, 1e-6))
            : saturate((t - 0.5 * (lo + hi)) / max(aa, 1e-6) + 0.5);
        alpha = lerp(alpha, alphas[i], blend);
    }
    return alpha;
}

float MaskAt(float2 pixel)
{
    int type = int(MaskType);
    if (type == 0) return 1.0;
    float3 p = float3(pixel, 1.0);
    float q = dot(MaskW.xyz, p);
    if (abs(q) < 1e-6) return 0.0;
    float2 uv = float2(dot(MaskU.xyz, p), dot(MaskV.xyz, p)) / q;
    if (type == 4)
    {
        float2 c = saturate(uv);
        return lerp(lerp(MaskAlpha0.x, MaskAlpha0.y, c.x), lerp(MaskAlpha0.z, MaskAlpha0.w, c.x), c.y);
    }
    float t = GradientSpread(GradientParam(type, MaskGeom0, MaskGeom1, uv), int(MaskParams.y));
    return MaskRamp(t, min(fwidth(t), fwidth(frac(t + 0.5))));
}

// What the layer drew at a point of its region (0..1 across it), with its mask and soft edge: premultiplied, nothing
// outside the region.
float4 LayerAt(float2 inRegion, float2 pixel)
{
    float inside = all(inRegion >= 0.0) && all(inRegion < 1.0) ? 1.0 : 0.0;
    float2 uv = saturate(inRegion) * LayerTexels.xy;
    float soft = 1.0;
    if (SoftOn > 0.5)
    {
        // The blurred coverage is one half on the outline and one deep inside: from 0 to 1 in between, smoothly.
        soft = saturate(2.0 * SoftTexture.SampleLevel(SourceSampler, uv, 0).r - 1.0);
    }

    float4 drawn = SourceTexture.SampleLevel(SourceSampler, uv, 0) * soft;
    float4 bands = BandsOn > 0.5 ? BandsTexture.SampleLevel(SourceSampler, uv, 0) : float4(0.0, 0.0, 0.0, 0.0);
    return (drawn + bands * (1.0 - drawn.a)) * (MaskAt(pixel) * inside);
}

// The reflection's strength on a window row: none above the floor, gathering to full by where the mirrored element
// begins - or half its sideways offset down, moved aside it meets the glow above in another place - full on to where
// its fade begins, nothing past its end.
float MirrorStrength(float row)
{
    float gather = max(max(Mirror.y - Mirror.w, MirrorQuad.w), 1.0);
    return Mirror.x * step(Mirror.w, row) * saturate((row - Mirror.w) / gather)
           * saturate((Mirror.z - row) / max(MirrorFade.x, 1.0));
}

struct LayerVSOutput
{
    float4 Position : SV_Position;
    float2 Uv : TEXCOORD0;
};

// A strip of four corners from the vertex id; no buffers. Vulkan's clip space points Y down, the way the window's rows
// run, so the box maps straight across.
[shader("vertex")]
LayerVSOutput LayerCompositeVS(uint id : SV_VertexID)
{
    LayerVSOutput o;
    o.Uv = float2(id & 1, (id >> 1) & 1);
    float2 pixel = LayerBox.xy + o.Uv * LayerBox.zw;
    o.Position = float4(pixel / max(ViewportSize, float2(1.0, 1.0)) * 2.0 - 1.0, 0.0, 1.0);
    // The reflection is placed upside down, so its top row samples the layer's bottom one.
    o.Uv.y = lerp(o.Uv.y, 1.0 - o.Uv.y, step(1.5, Role()));
    return o;
}

// The layer was blended onto transparent black, so its color already carries its alpha: scaled here as a whole, and by
// the mask and the soft edge where there are, and laid down with the premultiplied "over".
//
// Below the floor, where the reflection lies, ONE draw lays down both: what the element itself draws there (its glow,
// its shadow) and the mirror image, mixed by the reflection's strength. Laid one over the other they added up, and the
// glow came out brighter below the floor than above it - a seam along it.
[shader("fragment")]
float4 LayerCompositePS(LayerVSOutput input) : SV_Target
{
    float2 here = SurfacePixel(input.Position.xy);
    float2 hereInRegion = (here - SourceRegion.xy) / max(SourceRegion.zw, float2(1.0, 1.0));
    float role = Role();
    float4 self = LayerAt(hereInRegion, here);
    float4 mirror = LayerAt(input.Uv, SourceRegion.xy + input.Uv * SourceRegion.zw);
    float under = role > 0.5 && here.y >= Mirror.w && here.y < MirrorQuad.z
                  && here.x >= MirrorQuad.x && here.x < MirrorQuad.y ? 1.0 : 0.0;
    // Only where the element itself leaves room: what it draws solid stays in front of the floor.
    float4 laid = role < 1.5 ? self * (1.0 - under)
                             : lerp(self, mirror, MirrorStrength(here.y) * (1.0 - self.a)) * under;
    return laid * LayerAlpha;
}

technique LayerComposite
{
    pass Draw
    {
        VertexShader = LayerCompositeVS;
        PixelShader = LayerCompositePS;
    }
}
