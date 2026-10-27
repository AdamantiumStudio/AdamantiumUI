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

// Soft edges: the element's box in device pixels (.xy) and how far in from its outline it fades away (.z), through the
// same box mapping as the mask; zero = a hard edge. Its corners in device pixels: TL, TR, BR, BL.
float4 Soft;
float4 SoftRadii;

// The layer's own region on the window, in device pixels: where a texel of it was drawn.
float4 SourceRegion;

// The reflection draw (.w = 1): the layer laid down mirrored under the element, .x strong where it starts on window row .y
// and gone .z pixels further down.
float4 Mirror;

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

// From its outline in, rounded as its corners are: 1 deeper than the fade, 0 on the outline and outside.
float SoftAt(float2 pixel)
{
    if (Soft.z <= 0.0) return 1.0;
    float3 p = float3(pixel, 1.0);
    float q = dot(MaskW.xyz, p);
    if (abs(q) < 1e-6) return 0.0;
    float2 box = float2(dot(MaskU.xyz, p), dot(MaskV.xyz, p)) / q;
    float2 extent = Soft.xy * 0.5;
    float2 local = (box - 0.5) * Soft.xy;
    float r = local.x < 0.0 ? (local.y < 0.0 ? SoftRadii.x : SoftRadii.w) : (local.y < 0.0 ? SoftRadii.y : SoftRadii.z);
    r = min(r, min(extent.x, extent.y));
    float2 corner = abs(local) - extent + r;
    float d = min(max(corner.x, corner.y), 0.0) + length(max(corner, float2(0.0, 0.0))) - r;
    return saturate(-d / Soft.z);
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
    o.Uv.y = lerp(o.Uv.y, 1.0 - o.Uv.y, Mirror.w);
    return o;
}

// The layer was blended onto transparent black, so its color already carries its alpha: scaled here as a whole, and by
// the mask and the soft edge where there are, and laid down with the premultiplied "over". The reflection asks both of
// the pixel it mirrors, and fades from where it starts on down.
[shader("fragment")]
float4 LayerCompositePS(LayerVSOutput input) : SV_Target
{
    float2 here = SurfacePixel(input.Position.xy);
    float2 source = lerp(here, SourceRegion.xy + input.Uv * SourceRegion.zw, Mirror.w);
    float below = here.y - Mirror.y;
    float fade = lerp(1.0, Mirror.x * step(0.0, below) * saturate(1.0 - below / max(Mirror.z, 1.0)), Mirror.w);
    float mask = MaskAt(source) * SoftAt(source) * fade;
    return SourceTexture.SampleLevel(SourceSampler, input.Uv * LayerTexels.xy, 0) * (LayerAlpha * mask);
}

technique LayerComposite
{
    pass Draw
    {
        VertexShader = LayerCompositeVS;
        PixelShader = LayerCompositePS;
    }
}
