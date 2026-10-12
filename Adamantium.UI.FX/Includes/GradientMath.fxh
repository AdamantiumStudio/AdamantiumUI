// ---- GRADIENT PARAMETER --------------------------------------------------------------------------------------------
// Where a point falls along a gradient, from the geometry GradientBake packs: type 1 linear (geom0 = startXY, endXY),
// 2 radial (geom0 = centerXY, radiusXY; geom1 = originXY), 3 conic (geom0 = centerXY, start angle in turns). uv is the
// point in the filled box, 0..1.

// Past the ends: 0 pad, 1 reflect, 2 repeat.
float GradientSpread(float t, int spread)
{
    if (spread == 1) { float m = fmod(abs(t), 2.0); return m > 1.0 ? 2.0 - m : m; }
    if (spread == 2) { return frac(t); }
    return saturate(t);
}

float GradientParam(int type, float4 geom0, float4 geom1, float2 uv)
{
    if (type == 2)
    {
        float2 center = geom0.xy;
        float2 radius = max(geom0.zw, float2(1e-4, 1e-4));
        float2 focal = (geom1.xy - center) / radius;   // focal in unit-circle space
        float2 q = (uv - center) / radius;
        float2 dir = q - focal;
        float dlen = length(dir);
        if (dlen < 1e-6) return 0.0;
        float2 dn = dir / dlen;
        float b = dot(focal, dn);
        float c = dot(focal, focal) - 1.0;
        float sEdge = -b + sqrt(max(b * b - c, 0.0));      // dist focal->unit-circle along the ray
        return (sEdge > 1e-6) ? (dlen / sEdge) : 0.0;
    }
    if (type == 3)   // conic: 0 at top (12 o'clock), + is clockwise (screen y is down)
    {
        float2 d = uv - geom0.xy;
        float ang = atan2(d.x, -d.y);
        return frac(ang * 0.15915494309 - geom0.z);
    }
    float2 start = geom0.xy;
    float2 axis = geom0.zw - start;
    float denom = dot(axis, axis);
    if (denom < 1e-9) return 0.0;
    return dot(uv - start, axis) / denom;
}
