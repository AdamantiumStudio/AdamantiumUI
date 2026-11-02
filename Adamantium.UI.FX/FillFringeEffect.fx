// Analytic AA for fills: a coverage ring around a closed contour, drawn over the solid body. The ring (FringeGeometry.cs)
// is scale-free; the VS builds a screen-space miter from edge directions, so width holds at any transform.

#include "Includes/GradientMath.fxh"

float4x4 Projection;
float4 FillColor;
float2 ViewportSize;      // render target size in DEVICE pixels - the NDC <-> pixel basis for the fringe offset
float FringePixels;       // fringe width in DEVICE pixels (1 = the analytic-AA edge is exactly one pixel wide)

// Gradient-aware fringe: when IsGradient != 0 the ring is colored by the SAME linear/radial gradient as the fill (so the
// feathered edge matches the fill color there, not one flat color) - the fix for aliased gradient-shape edges. The
// gradient is passed as plain uniforms (per-draw, no BDA / heavy interpolators). The parameter is GradientMath's; the
// ramp mirrors BrushEffect's GradColor.
int IsGradient;
float4 GParams;       // (_, type[1 linear/2 radial/3 conic], stopCount, spread)
float4 GGeom0;        // LOCAL 0..1: linear (startXY,endXY) | radial (centerXY,radiusXY)
float4 GGeom1;        // radial focal (originXY,_,_)
float4 GLocalBounds;  // shape local bounds: minXY, sizeXY (uv = (local-min)/size)
float4 GS0; float4 GS1; float4 GS2; float4 GS3; float4 GS4; float4 GS5; float4 GS6; float4 GS7;
float4 GOff0; float4 GOff1;

float4 FringeGradColor(float t)
{
    int n = int(GParams.z);
    if (n <= 0) return float4(0.0, 0.0, 0.0, 0.0);
    float offs[8];
    offs[0]=GOff0.x; offs[1]=GOff0.y; offs[2]=GOff0.z; offs[3]=GOff0.w;
    offs[4]=GOff1.x; offs[5]=GOff1.y; offs[6]=GOff1.z; offs[7]=GOff1.w;
    float4 cols[8];
    cols[0]=GS0; cols[1]=GS1; cols[2]=GS2; cols[3]=GS3; cols[4]=GS4; cols[5]=GS5; cols[6]=GS6; cols[7]=GS7;
    if (t <= offs[0]) return cols[0];
    for (int i = 1; i < n; i++)
        if (t <= offs[i]) { float seg = max(offs[i] - offs[i-1], 1e-6); return lerp(cols[i-1], cols[i], saturate((t - offs[i-1]) / seg)); }
    return cols[n - 1];
}

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
    float4 c;
    if (IsGradient != 0)
    {
        float2 uv = (input.Local - GLocalBounds.xy) / max(GLocalBounds.zw, float2(1e-4, 1e-4));
        c = FringeGradColor(GradientSpread(GradientParam(int(GParams.y), GGeom0, GGeom1, uv), int(GParams.w)));
    }
    else
    {
        c = FillColor;
    }
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
