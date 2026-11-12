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

// Where a piece running one way in y reaches the line y = value.
float CurveXAt(float2 start, float2 control, float2 end, float value)
{
    return CurveAt(start.x, control.x, end.x, CurveCrossing(start.y, control.y, end.y, value));
}

// Where a piece running one way in x reaches the line x = value.
float CurveYAt(float2 start, float2 control, float2 end, float value)
{
    return CurveAt(start.y, control.y, end.y, CurveCrossing(start.x, control.x, end.x, value));
}

// How a crossing distance pixels from the pixel's centre along a ray softens the winding there: by how much of the
// pixel lies before it, less the whole crossing when the hard count has it - one past the centre.
float CurveSoftening(float distance)
{
    return saturate(distance + 0.5) - (distance > 0.0 ? 1.0 : 0.0);
}

// How much of the pixel at local - the fill's local space - the fill covers, 0..1. Takes derivatives, so it is called
// where every fragment of the quad still runs.
//
// Down from the root to the tile holding the pixel, while the next is no smaller than the pixel: a tile's half-side
// margin then holds every piece within half a pixel. From the tile's point the winding is carried along its row to under
// the pixel, then up or down its column to it: toward +x a piece passed going right is lost and one passed going left
// gained, and the same toward +y. Near the pixel each piece crossed is softened over the pixel it falls in; each ray
// softens only the edges it crosses, and the one with a crossing nearer the pixel speaks for it.
float CurveTileCoverage(float2 local)
{
    uint* words = CurveData;
    float2 position = local - CurvePoint(0u);
    float2 acrossScreenX = ddx(position);
    float2 acrossScreenY = ddy(position);
    float unitsPerPixelX = length(float2(acrossScreenX.x, acrossScreenY.x));
    float unitsPerPixelY = length(float2(acrossScreenX.y, acrossScreenY.y));
    float pixelsPerUnitX = 1.0 / max(unitsPerPixelX, 1e-20);
    float pixelsPerUnitY = 1.0 / max(unitsPerPixelY, 1e-20);
    float pixelSide = max(unitsPerPixelX, unitsPerPixelY);

    uint tile = words[7];
    float tileSide = asfloat(words[6]);
    float2 tileCorner = float2(0.0, 0.0);
    [loop]
    for (uint depth = 0u; depth < 24u; depth++)
    {
        uint firstChild = words[tile + 5u];
        if (firstChild == 0u || tileSide * 0.5 < pixelSide)
        {
            break;
        }

        tileSide *= 0.5;
        uint2 quarter = uint2(position.x >= tileCorner.x + tileSide ? 1u : 0u,
                              position.y >= tileCorner.y + tileSide ? 1u : 0u);
        tileCorner += float2(quarter) * tileSide;
        tile = firstChild + (quarter.y * 2u + quarter.x) * CurveTileWords;
    }

    float2 tilePoint = float2(asfloat(words[tile]), asfloat(words[tile + 1u]));
    float hardWinding = float(asint(words[tile + 2u]));
    uint listStart = words[tile + 3u];
    uint listCount = words[tile + 4u];

    float softX = 0.0;
    float softY = 0.0;
    float nearestX = 1e20;
    float nearestY = 1e20;
    for (uint entry = 0u; entry < listCount; entry++)
    {
        uint at = CurveHeaderWords + words[listStart + entry] * CurvePieceWords;
        float2 start = CurvePoint(at);
        float2 control = CurvePoint(at + 2u);
        float2 end = CurvePoint(at + 4u);
        float turnX = end.y > start.y ? 1.0 : -1.0;
        float turnY = end.x < start.x ? 1.0 : -1.0;

        if (tilePoint.y >= min(start.y, end.y) && tilePoint.y < max(start.y, end.y))
        {
            float hitX = CurveXAt(start, control, end, tilePoint.y);
            if (position.x > tilePoint.x && hitX > tilePoint.x && hitX <= position.x) hardWinding -= turnX;
            if (position.x < tilePoint.x && hitX > position.x && hitX <= tilePoint.x) hardWinding += turnX;
        }

        if (position.x >= min(start.x, end.x) && position.x < max(start.x, end.x))
        {
            float hitY = CurveYAt(start, control, end, position.x);
            if (position.y > tilePoint.y && hitY > tilePoint.y && hitY <= position.y) hardWinding -= turnY;
            if (position.y < tilePoint.y && hitY > position.y && hitY <= tilePoint.y) hardWinding += turnY;
            float distance = (hitY - position.y) * pixelsPerUnitY;
            softY += turnY * CurveSoftening(distance);
            nearestY = min(nearestY, abs(distance));
        }

        if (position.y >= min(start.y, end.y) && position.y < max(start.y, end.y))
        {
            float distance = (CurveXAt(start, control, end, position.y) - position.x) * pixelsPerUnitX;
            softX += turnX * CurveSoftening(distance);
            nearestX = min(nearestX, abs(distance));
        }
    }

    uint evenOdd = words[5];
    float coverX = CurveWindingCoverage(hardWinding + softX, evenOdd);
    float coverY = CurveWindingCoverage(hardWinding + softY, evenOdd);
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
