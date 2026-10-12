// A LAYER laid down: a subtree drawn at full strength into a texture of its own, put back over the window ONCE with the
// group's opacity - so where its children overlap, the background shows through as much as anywhere else.

#include "Includes/CommonData.fxh"

// Where the layer goes, in device pixels from the viewport's corner: .xy its top-left corner, .zw its size. The texture
// holds exactly that region, so a texel lands on a pixel.
float4 LayerBox;

// How much of the texture the layer took (.xy, 0..1): textures only grow, so a smaller layer uses its top-left part.
float4 LayerTexels;

// The group's opacity with every ancestor's folded in.
float LayerAlpha;

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

// The layer was blended onto transparent black, so its color already carries its alpha: scaled here as a whole and laid
// down with the premultiplied "over".
[shader("fragment")]
float4 LayerCompositePS(LayerVSOutput input) : SV_Target
{
    return SourceTexture.SampleLevel(SourceSampler, input.Uv * LayerTexels.xy, 0) * LayerAlpha;
}

technique LayerComposite
{
    pass Draw
    {
        EffectName = "LayerCompositeEffect";
        VertexShader = LayerCompositeVS;
        PixelShader = LayerCompositePS;
    }
}
