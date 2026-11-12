// CURVE FILL MATHS - a fill drawn from its curves rather than from a mesh. One quad per instance over the fill's box;
// each fragment counts how its curves wind round it, along a ray to +x and a ray to +y, each crossing softened over the
// pixel it falls in - exact at any zoom, with no mesh and no fringe. The curves are quadratic pieces running one way in
// x and in y. A fill takes its coverage from its own tiles (CurveTiles.fxh); the walk over every piece here is what the
// tiles are checked against. Every brush that fills a shape this way takes its coverage from these and its color from
// its own maths. Include after ShapeMath.fxh.

static const uint CurveHeaderWords = 8u;

// The packed curves of the fill the draw is of - see Retained/CurveFill.cs for the layout.
uint* CurveData;

float CurveWord(uint i)
{
    uint* words = CurveData;
    return asfloat(words[i]);
}

float2 CurvePoint(uint i)
{
    return float2(CurveWord(i), CurveWord(i + 1u));
}

// One corner of the quad an instance draws, in the fill's local space - the space its mesh's vertices are in: the
// pieces' box, a pixel past it on every side so its edge's soft half lands inside. toSlot takes that space to the slot's.
float2 CurveQuadCorner(uint vertexId, float4x4 toSlot)
{
    float2 px = SlotPixelScale(toSlot);
    float2 corner = float2(vertexId & 1u, (vertexId >> 1u) & 1u);
    return CurvePoint(0u) + corner * CurvePoint(2u) + (corner * 2.0 - 1.0) / px;
}

// Where along a piece running one way its coordinate - start a, control b, end c - reaches v.
float CurveCrossing(float a, float b, float c, float v)
{
    float qa = a - 2.0 * b + c;
    float qb = 2.0 * (b - a);
    float qc = a - v;
    float t;
    if (abs(qa) < 1e-6 * (abs(a) + abs(b) + abs(c) + 1.0))
    {
        t = abs(qb) > 1e-20 ? -qc / qb : 0.0;
    }
    else
    {
        float root = sqrt(max(qb * qb - 4.0 * qa * qc, 0.0));
        float t1 = (-qb + root) / (2.0 * qa);
        float t2 = (-qb - root) / (2.0 * qa);
        t = (t1 >= -1e-4 && t1 <= 1.0001) ? t1 : t2;
    }
    return saturate(t);
}

float CurveAt(float a, float b, float c, float t)
{
    float rest = 1.0 - t;
    return a * rest * rest + b * 2.0 * rest * t + c * t * t;
}

// The winding along one ray from p - relative to the pieces' box corner - each crossing counted by how much of the
// pixel lies before it, and how near the nearest crossing is in pixels. axis 0: the ray runs to +x; axis 1: to +y.
float2 CurveRay(float2 p, float pixelsPerUnit, uint axis)
{
    uint* words = CurveData;
    uint pieces = words[4];
    float along = axis == 0u ? p.x : p.y;
    float across = axis == 0u ? p.y : p.x;

    float winding = 0.0;
    float nearest = 1e20;
    for (uint i = 0u; i < pieces; i++)
    {
        uint at = CurveHeaderWords + i * 6u;
        float2 s = CurvePoint(at);
        float2 c = CurvePoint(at + 2u);
        float2 e = CurvePoint(at + 4u);
        float sa = axis == 0u ? s.x : s.y;
        float ea = axis == 0u ? e.x : e.y;
        // A piece ending half a pixel short of the ray's start does not count.
        if ((max(sa, ea) - along) * pixelsPerUnit < -0.5)
        {
            continue;
        }

        float sc = axis == 0u ? s.y : s.x;
        float cc = axis == 0u ? c.y : c.x;
        float ec = axis == 0u ? e.y : e.x;
        if (across < min(sc, ec) || across >= max(sc, ec))
        {
            continue;
        }

        float t = CurveCrossing(sc, cc, ec, across);
        float hit = CurveAt(sa, axis == 0u ? c.x : c.y, ea, t);
        float distance = (hit - along) * pixelsPerUnit;
        // Going down crossed to +x winds one way; going left crossed to +y the same way round.
        float turn = axis == 0u ? (ec > sc ? 1.0 : -1.0) : (ec < sc ? 1.0 : -1.0);
        winding += turn * saturate(distance + 0.5);
        nearest = min(nearest, abs(distance));
    }

    return float2(winding, nearest);
}

float CurveWindingCoverage(float winding, uint evenOdd)
{
    float w = abs(winding);
    return evenOdd != 0u ? 1.0 - abs(1.0 - fmod(w, 2.0)) : saturate(w);
}

// How much of the pixel at local - in the fill's local space, as CurveQuadCorner gives it - the fill covers, 0..1.
// Takes derivatives, so it is called where every fragment of the quad still runs: before any discard.
float CurveFillCoverage(float2 local)
{
    uint* words = CurveData;
    float2 p = local - CurvePoint(0u);
    float2 dx = ddx(p);
    float2 dy = ddy(p);
    // Pixels per fill unit along each axis of the fill, however the instance and its slot turn and scale it.
    float perX = 1.0 / max(length(float2(dx.x, dy.x)), 1e-20);
    float perY = 1.0 / max(length(float2(dx.y, dy.y)), 1e-20);

    float2 rayX = CurveRay(p, perX, 0u);
    float2 rayY = CurveRay(p, perY, 1u);
    uint evenOdd = words[5];
    float coverX = CurveWindingCoverage(rayX.x, evenOdd);
    float coverY = CurveWindingCoverage(rayY.x, evenOdd);
    // Each ray softens only the edges it crosses; the one with a crossing nearer the pixel speaks for it. Far from
    // every edge both agree.
    float weightX = saturate(1.0 - rayX.y);
    float weightY = saturate(1.0 - rayY.y);
    float weights = weightX + weightY;
    return weights > 1e-4 ? (coverX * weightX + coverY * weightY) / weights : coverX;
}
