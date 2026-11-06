// Analytic AA for fills: a coverage ring around a closed contour, drawn over the solid body. The ring (FringeGeometry.cs)
// is scale-free; the VS builds a screen-space miter from edge directions, so width holds at any transform.

#include "Includes/GradientMath.fxh"

float4x4 Projection;
float4 FillColor;
float2 ViewportSize;      // render target size in DEVICE pixels - the NDC <-> pixel basis for the fringe offset
float FringePixels;       // fringe width in DEVICE pixels (1 = the analytic-AA edge is exactly one pixel wide)

// Gradient-aware fringe: when IsGradient != 0 the ring is colored by the SAME gradient as the fill (so the feathered
// edge matches the fill color there, not one flat color) - the fix for aliased gradient-shape edges.
#include "Includes/GradientUniforms.fxh"

#include "Includes/Ring.fxh"

struct PSInput { float4 Position : SV_Position; float Coverage : TEXCOORD0; float2 Local : TEXCOORD1; };

[shader("vertex")]
PSInput FillFringeVS(uint vertexId : SV_VertexID)
{
    FringeVertex v = RingCorner(vertexId);
    PSInput o;
    float coverage;
    o.Position = ExpandFringe(v, Projection, coverage);
    o.Coverage = coverage;
    o.Local = v.Position;   // LOCAL geometry position -> the PS maps it to the gradient uv
    return o;
}

[shader("fragment")]
float4 FillFringePS(PSInput input) : SV_Target
{
    // Gradient fills color the ring by the gradient at this fragment (matching the fill); solid fills use FillColor.
    float4 c = IsGradient != 0 ? GradientColorAt(input.Local) : FillColor;
    c.a *= saturate(input.Coverage);   // 1 at the contour -> 0 at the outer fringe = analytic edge coverage
    return c;
}

technique FillFringe
{
    // Draw (graphics): rasterize the fringe ring; alpha ramps with coverage.
    pass Draw
    {
        VertexShader = FillFringeVS;
        PixelShader = FillFringePS;
    }
}
