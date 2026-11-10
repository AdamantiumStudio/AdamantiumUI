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

// What GradientParam answers where a gradient between two circles has no circle passing: nothing is painted there.
static const float GradientNowhere = -1e30;

float GradientParam(int type, float4 geom0, float4 geom1, float2 uv)
{
    // Between two circles (Canvas 2D createRadialGradient): in the end ellipse's unit space the start circle stands at
    // the origin point with radius geom1.z, the end circle at 0 with radius 1, and t is the largest at which the circle
    // c(t), r(t) >= 0, passes through the point.
    if (type == 5)
    {
        float2 center = geom0.xy;
        float2 radius = max(geom0.zw, float2(1e-4, 1e-4));
        float2 focal = (geom1.xy - center) / radius;
        float r0 = geom1.z;
        float2 p = (uv - center) / radius - focal;
        float2 cd = -focal;
        float dr = 1.0 - r0;
        float a = dot(cd, cd) - dr * dr;
        float b = dot(p, cd) + r0 * dr;
        float c = dot(p, p) - r0 * r0;
        if (abs(a) < 1e-6)
        {
            if (abs(b) < 1e-9) return GradientNowhere;
            float t = c / (2.0 * b);
            return r0 + t * dr >= 0.0 ? t : GradientNowhere;
        }

        float disc = b * b - a * c;
        if (disc < 0.0) return GradientNowhere;
        float s = sqrt(disc);
        float t1 = (b + s) / a;
        float t2 = (b - s) / a;
        float hi = max(t1, t2);
        float lo = min(t1, t2);
        if (r0 + hi * dr >= 0.0) return hi;
        return r0 + lo * dr >= 0.0 ? lo : GradientNowhere;
    }

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
