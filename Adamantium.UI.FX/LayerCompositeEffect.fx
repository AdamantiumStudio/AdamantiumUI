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
    return o;
}

// The layer was blended onto transparent black, so its color already carries its alpha: scaled here as a whole, and by
// the mask where there is one, and laid down with the premultiplied "over".
[shader("fragment")]
float4 LayerCompositePS(LayerVSOutput input) : SV_Target
{
    float mask = MaskAt(SurfacePixel(input.Position.xy));
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
