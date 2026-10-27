// SOFT EDGES: how deep inside what a layer drew each pixel lies, as a Gaussian blur of its coverage - one axis per pass.
// Blurring the coverage rather than measuring a box is what lets the edge follow any outline the element draws: an
// ellipse, a star, a glyph, a picture's own transparency.

#include "Includes/CommonData.fxh"

// .xy one texel along the axis being blurred, in uv; .z the Gaussian's sigma in texels; .w what is read: 0 the layer's
// coverage (its alpha), 1 the previous pass's result (its red).
float4 BlurAxis;

// The part of the texture the layer took (.xy, 0..1) and one texel (.zw): reads are kept inside it, beyond lies an
// older drawing.
float4 BlurLimit;

// The region's sides (left, top, right, bottom) where an ancestor's clip cut the drawing short, 1 for each: past those
// the drawing goes on, past the others nothing was drawn.
float4 BlurCut;

struct SoftVSOutput
{
    float4 Position : SV_Position;
    float2 Uv : TEXCOORD0;
};

// One oversized triangle over the layer's region.
[shader("vertex")]
SoftVSOutput LayerSoftEdgeVS(uint id : SV_VertexID)
{
    SoftVSOutput o;
    o.Uv = float2((id << 1) & 2, id & 2);
    o.Position = float4(o.Uv * 2.0 - 1.0, 0.0, 1.0);
    return o;
}

// Beyond the region nothing was drawn - an element that clips itself fills its region to the edge, and reading the edge
// on from there would make it go on forever - except where a clip cut it, which is no outline of its own.
float Coverage(float2 uv)
{
    bool empty = (uv.x < 0.0 && BlurCut.x < 0.5) || (uv.y < 0.0 && BlurCut.y < 0.5)
                 || (uv.x >= BlurLimit.x && BlurCut.z < 0.5) || (uv.y >= BlurLimit.y && BlurCut.w < 0.5);
    float inside = empty ? 0.0 : 1.0;
    float2 inset = BlurLimit.zw * 0.5;
    float4 texel = SourceTexture.SampleLevel(SourceSampler, clamp(uv, inset, BlurLimit.xy - inset), 0);
    // Half covered counts as covered: the antialiased rim is the outline itself, not something beyond it.
    return lerp(saturate(texel.a * 2.0), texel.r, BlurAxis.w) * inside;
}

// Twenty-four taps a side, spread over three sigmas whatever the width, so a wide edge costs what a narrow one does.
[shader("fragment")]
float4 LayerSoftEdgePS(SoftVSOutput input) : SV_Target
{
    const int taps = 24;
    float2 uv = input.Uv * BlurLimit.xy;
    float sigma = max(BlurAxis.z, 0.5);
    float stride = max(1.0, 3.0 * sigma / taps);
    float sum = Coverage(uv);
    float total = 1.0;
    for (int i = 1; i <= taps; i++)
    {
        float d = i * stride;
        float w = exp(-d * d / (2.0 * sigma * sigma));
        sum += w * (Coverage(uv + BlurAxis.xy * d) + Coverage(uv - BlurAxis.xy * d));
        total += 2.0 * w;
    }

    return float4(sum / total, 0.0, 0.0, 1.0);
}

technique LayerSoftEdge
{
    pass Blur
    {
        VertexShader = LayerSoftEdgeVS;
        PixelShader = LayerSoftEdgePS;
    }
}
