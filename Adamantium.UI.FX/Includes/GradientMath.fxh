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

// How much of the pixel lies where `measure` >= 0: half on the line, the rest by its slope across the pixel.
float CoverageOf(float measure)
{
    return saturate(0.5 + measure / max(fwidth(measure), 1e-20));
}

// Between two circles (Canvas 2D createRadialGradient): in the end ellipse's unit space the start circle stands at the
// origin point with radius geom1.z, the end circle at 0 with radius 1, and t is the largest at which the circle c(t),
// r(t) >= 0, passes through the point. Where none passes, t is the nearest edge's; `reach` and `radius` are what say so -
// a circle passes where both are >= 0 - and they change smoothly across that edge, so a fragment stage can cover it
// (TwoCirclesCoverage). No derivatives here: any stage, any branch.
float TwoCirclesRoot(float4 geom0, float4 geom1, float2 uv, out float reach, out float radius)
{
    float2 center = geom0.xy;
    float2 extent = max(geom0.zw, float2(1e-4, 1e-4));
    float2 focal = (geom1.xy - center) / extent;
    float r0 = geom1.z;
    float2 p = (uv - center) / extent - focal;
    float2 cd = -focal;
    float dr = 1.0 - r0;
    float a = dot(cd, cd) - dr * dr;
    float b = dot(p, cd) + r0 * dr;
    float c = dot(p, p) - r0 * r0;
    if (abs(a) < 1e-6)   // the same for every pixel: a is the circles' alone
    {
        float side = b >= 0.0 ? 1.0 : -1.0;
        float linear = c / (2.0 * side * max(abs(b), 1e-9));
        reach = 1.0;
        radius = r0 + linear * dr;
        return linear;
    }

    reach = b * b - a * c;
    // q / a and c / q: neither root is a difference of near-equal numbers, so a near 0 loses nothing.
    float q = b + (b >= 0.0 ? 1.0 : -1.0) * sqrt(max(reach, 0.0));
    float safeQ = abs(q) < 1e-12 ? 1e-12 : q;
    float hi = max(q / a, c / safeQ);
    float lo = min(q / a, c / safeQ);
    float hiRadius = r0 + hi * dr;
    float loRadius = r0 + lo * dr;
    radius = max(hiRadius, loRadius);
    // The larger t whose circle has a radius; past both, the one nearer to having one.
    float t = hiRadius >= 0.0 || hiRadius >= loRadius ? hi : lo;
    return abs(q) < 1e-12 ? 0.0 : t;
}

// How much of the pixel the circles reach, from TwoCirclesRoot's `reach` and `radius`. Fragment stages, in control flow
// every pixel of a quad shares.
float TwoCirclesCoverage(float reach, float radius)
{
    return CoverageOf(reach) * CoverageOf(radius);
}

// The parameter and the pixel's coverage of a gradient between two circles, for a fragment stage.
float TwoCirclesParam(float4 geom0, float4 geom1, float2 uv, out float covered)
{
    float reach;
    float radius;
    float t = TwoCirclesRoot(geom0, geom1, uv, reach, radius);
    covered = TwoCirclesCoverage(reach, radius);
    return t;
}

float GradientParam(int type, float4 geom0, float4 geom1, float2 uv)
{
    if (type == 5)
    {
        float reach;
        float radius;
        float t = TwoCirclesRoot(geom0, geom1, uv, reach, radius);
        return reach >= 0.0 && radius >= 0.0 ? t : GradientNowhere;
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
