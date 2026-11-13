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

// Between two circles (Canvas 2D createRadialGradient): the largest t whose circle, r(t) >= 0, passes through the point.
// A circle passes where `edge` >= 0 and `passes` is 1; `edge` changes sign smoothly across the cone's side. Any stage.
float TwoCirclesRoot(float4 geom0, float4 geom1, float2 uv, out float edge, out float passes)
{
    float2 center = geom0.xy;
    float2 extent = max(geom0.zw, float2(1e-4, 1e-4));
    float2 focal = (geom1.xy - center) / extent;
    float startRadius = geom1.z;
    float2 place = (uv - center) / extent - focal;
    float2 toEnd = -focal;
    float growth = 1.0 - startRadius;
    float leading = dot(toEnd, toEnd) - growth * growth;
    float middle = dot(place, toEnd) + startRadius * growth;
    float trailing = dot(place, place) - startRadius * startRadius;
    if (abs(leading) < 1e-6)
    {
        float side = middle >= 0.0 ? 1.0 : -1.0;
        float only = trailing / (2.0 * side * max(abs(middle), 1e-9));
        edge = startRadius + only * growth;
        passes = 1.0;
        return only;
    }

    float reach = middle * middle - leading * trailing;
    if (reach < 0.0)
    {
        float meeting = middle / leading;
        edge = reach;
        passes = startRadius + meeting * growth >= 0.0 ? 1.0 : 0.0;
        return meeting;
    }

    float stable = middle + (middle >= 0.0 ? 1.0 : -1.0) * sqrt(reach);
    if (abs(stable) < 1e-12)
    {
        edge = 1.0;
        passes = 1.0;
        return 0.0;
    }

    float high = max(stable / leading, trailing / stable);
    float low = min(stable / leading, trailing / stable);
    float highRadius = startRadius + high * growth;
    float lowRadius = startRadius + low * growth;
    edge = leading < 0.0 ? 1.0 : reach;
    passes = highRadius >= 0.0 || lowRadius >= 0.0 ? 1.0 : 0.0;
    return highRadius >= 0.0 ? high : low;
}

// How much of the pixel the circles reach; fragment stages, in flow the whole quad shares.
float TwoCirclesCoverage(float edge, float passes)
{
    return CoverageOf(edge) * passes;
}

// The parameter and the pixel's coverage of a gradient between two circles, for a fragment stage.
float TwoCirclesParam(float4 geom0, float4 geom1, float2 uv, out float covered)
{
    float edge;
    float passes;
    float t = TwoCirclesRoot(geom0, geom1, uv, edge, passes);
    covered = TwoCirclesCoverage(edge, passes);
    return t;
}

float GradientParam(int type, float4 geom0, float4 geom1, float2 uv)
{
    if (type == 5)
    {
        float edge;
        float passes;
        float t = TwoCirclesRoot(geom0, geom1, uv, edge, passes);
        return edge >= 0.0 && passes > 0.0 ? t : GradientNowhere;
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
