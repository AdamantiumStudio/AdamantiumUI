// CURVE FILLS BY THEIR OWN TILES - a fill's pieces put once into a tree of square tiles of its own space, a tile with a
// long list cut into four (see Retained/CurveFill.cs and CurveTileTree.cs). A tile lists the pieces within half its side
// of it and holds the exact winding at a point of its own. A fragment goes down to the tile holding it, no smaller than
// its pixel, counts its winding from the tile's point along a path inside the tile - only the tile's pieces can cross
// it - and softens the edges by the listed pieces within half a pixel. Nothing depends on how the fill is placed: moved,
// turned, scaled or seen in perspective, it is drawn from the same tiles. Include after CurveMath.fxh.

static const uint CurveTileWords = 6u;

// Whether the draw's coverage comes from the fill's tiles (1) or from a walk over its every piece (0) - the walk is what
// the tiles are checked against.
uint CurveTiled;

// Where a piece crossing the line of y = v reaches it - the piece running one way in y.
float CurveXAt(float2 s, float2 c, float2 e, float v)
{
    return CurveAt(s.x, c.x, e.x, CurveCrossing(s.y, c.y, e.y, v));
}

float CurveYAt(float2 s, float2 c, float2 e, float v)
{
    return CurveAt(s.y, c.y, e.y, CurveCrossing(s.x, c.x, e.x, v));
}

// How much of the pixel at local - the fill's local space - the fill covers, 0..1. Takes derivatives, so it is called
// where every fragment of the quad still runs.
float CurveTileCoverage(float2 local)
{
    uint* words = CurveData;
    float2 p = local - CurvePoint(0u);
    float2 dx = ddx(p);
    float2 dy = ddy(p);
    // The fill's units a pixel spans along each of its axes, however it is turned, scaled or seen.
    float unitsX = length(float2(dx.x, dy.x));
    float unitsY = length(float2(dx.y, dy.y));
    float perX = 1.0 / max(unitsX, 1e-20);
    float perY = 1.0 / max(unitsY, 1e-20);
    float pixel = max(unitsX, unitsY);

    // Down from the root to the tile holding the pixel, while the next is no smaller than the pixel: a tile's half-side
    // margin then holds every piece within half a pixel.
    uint tile = words[7];
    float side = asfloat(words[6]);
    float2 corner = float2(0.0, 0.0);
    [loop]
    for (uint depth = 0u; depth < 24u; depth++)
    {
        uint child = words[tile + 5u];
        if (child == 0u || side * 0.5 < pixel)
        {
            break;
        }

        side *= 0.5;
        uint2 quarter = uint2(p.x >= corner.x + side ? 1u : 0u, p.y >= corner.y + side ? 1u : 0u);
        corner += float2(quarter) * side;
        tile = child + (quarter.y * 2u + quarter.x) * CurveTileWords;
    }

    float2 q = float2(asfloat(words[tile]), asfloat(words[tile + 1u]));
    float hard = float(asint(words[tile + 2u]));
    uint first = words[tile + 3u];
    uint count = words[tile + 4u];

    float softX = 0.0;
    float softY = 0.0;
    float nearestX = 1e20;
    float nearestY = 1e20;
    for (uint i = 0u; i < count; i++)
    {
        uint at = CurveHeaderWords + words[first + i] * 6u;
        float2 s = CurvePoint(at);
        float2 c = CurvePoint(at + 2u);
        float2 e = CurvePoint(at + 4u);
        float turnX = e.y > s.y ? 1.0 : -1.0;
        float turnY = e.x < s.x ? 1.0 : -1.0;

        // From the tile's point along its row to under the pixel: the winding toward +x loses a piece passed going
        // right and gains one passed going left.
        if (q.y >= min(s.y, e.y) && q.y < max(s.y, e.y))
        {
            float hit = CurveXAt(s, c, e, q.y);
            if (p.x > q.x && hit > q.x && hit <= p.x) hard -= turnX;
            if (p.x < q.x && hit > p.x && hit <= q.x) hard += turnX;
        }

        // Then up or down its column to the pixel, the same way toward +y; and, near the pixel, each piece crossed is
        // softened over the pixel it falls in.
        if (p.x >= min(s.x, e.x) && p.x < max(s.x, e.x))
        {
            float hit = CurveYAt(s, c, e, p.x);
            if (p.y > q.y && hit > q.y && hit <= p.y) hard -= turnY;
            if (p.y < q.y && hit > p.y && hit <= q.y) hard += turnY;
            float d = (hit - p.y) * perY;
            softY += turnY * (saturate(d + 0.5) - (d > 0.0 ? 1.0 : 0.0));
            nearestY = min(nearestY, abs(d));
        }

        if (p.y >= min(s.y, e.y) && p.y < max(s.y, e.y))
        {
            float d = (CurveXAt(s, c, e, p.y) - p.x) * perX;
            softX += turnX * (saturate(d + 0.5) - (d > 0.0 ? 1.0 : 0.0));
            nearestX = min(nearestX, abs(d));
        }
    }

    uint evenOdd = words[5];
    float coverX = CurveWindingCoverage(hard + softX, evenOdd);
    float coverY = CurveWindingCoverage(hard + softY, evenOdd);
    // Each ray softens only the edges it crosses; the one with a crossing nearer the pixel speaks for it.
    float weightX = saturate(1.0 - nearestX);
    float weightY = saturate(1.0 - nearestY);
    float weights = weightX + weightY;
    return weights > 1e-4 ? (coverX * weightX + coverY * weightY) / weights : coverX;
}

// The coverage of a curve fill's fragment at local - its place in the fill's local space. The choice is uniform over the
// draw, so either way's derivatives stay valid.
float CurveCoverage(float2 local)
{
    if (CurveTiled != 0u)
    {
        return CurveTileCoverage(local);
    }

    return CurveFillCoverage(local);
}
