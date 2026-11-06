// A linear/radial/conic gradient passed as plain per-draw uniforms (no BDA, no heavy interpolators), packed by
// GradientBake and set by GradientUniforms (C#). The parameter is GradientMath's; the ramp mirrors BrushEffect's
// GradColor. Include GradientMath.fxh before it.

int IsGradient;
float4 GParams;       // (_, type[1 linear/2 radial/3 conic/4 mesh], stopCount, spread)
float4 GGeom0;        // LOCAL 0..1: linear (startXY,endXY) | radial (centerXY,radiusXY)
float4 GGeom1;        // radial focal (originXY,_,_)
float4 GLocalBounds;  // shape local bounds: minXY, sizeXY (uv = (local-min)/size)
float4 GS0; float4 GS1; float4 GS2; float4 GS3; float4 GS4; float4 GS5; float4 GS6; float4 GS7;
float4 GOff0; float4 GOff1;

// The ramp's color at t, 0..1.
float4 GradientRamp(float t)
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

// The gradient's color at point `p` of the box it spans (minXY, sizeXY).
float4 GradientColorIn(float2 p, float4 box)
{
    float2 uv = (p - box.xy) / max(box.zw, float2(1e-4, 1e-4));
    if (int(GParams.y) == 4)   // mesh: the four corners (top-left, top-right, bottom-left, bottom-right) blended by uv
    {
        float2 c = saturate(uv);
        return lerp(lerp(GS0, GS1, c.x), lerp(GS2, GS3, c.x), c.y);
    }

    return GradientRamp(GradientSpread(GradientParam(int(GParams.y), GGeom0, GGeom1, uv), int(GParams.w)));
}

// The gradient's color at a point in the shape's local units, over the shape's bounds.
float4 GradientColorAt(float2 local)
{
    return GradientColorIn(local, GLocalBounds);
}
