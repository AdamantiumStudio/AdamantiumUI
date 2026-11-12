// CURVE FILLS BY SCREEN TILES - the compute side bins every curve fill of a flush into small screen tiles, each tile
// listing the pieces that pass within a pixel of it and holding, per pixel row, the winding of the pieces wholly to its
// right; a fragment then walks only its own tile's list. Include after CurveMath.fxh.

// One fill instance of the flush. Matches the C# CurveTileItem.
struct CurveTileItem
{
    float4 ToPixelX;     // the fill's local space to framebuffer pixels: x = dot(float3(p, 1), ToPixelX.xyz)
    float4 ToPixelY;     // y = dot(float3(p, 1), ToPixelY.xyz)
    uint* Curves;        // the fill's packed curves - see Retained/CurveFill.cs
    uint PieceFirst;     // its first piece among all the flush's
    uint PieceCount;
    int TileX;           // its tile grid's corner, in tiles from the framebuffer's
    int TileY;
    uint Columns;
    uint Rows;
    uint TileBase;       // its first tile in the tile arrays
    uint RowBase;        // its first tile row among all the flush's
    uint EvenOdd;
    uint Pad;
};

// A tile's width and height in pixels - CurveTiler.TileWidth and TileHeight. Low: a pixel walks only the pieces near its
// own few rows, and a piece is listed in as many tiles as rows of them it crosses.
static const int CurveTileWidth = 8;
static const int CurveTileHeight = 4;
static const float2 CurveTileSize = float2(8.0, 4.0);

// How near a tile a piece has to pass to be listed by it: a piece within half a pixel softens the tile's edge pixels.
static const float CurveTileMargin = 1.0;

// Whether the draw's coverage comes from the flush's tiles (1) or from a walk over each fill's every piece (0), and
// which of the flush's items its first instance is.
uint CurveTiled;
uint CurveTileItemBase;

CurveTileItem* CurveTileItems;
uint* CurveTileCounts;       // per tile: pieces listed
uint* CurveTileOffsets;      // per tile: where its list starts in CurveTileEntries
int* CurveTileBackdrops;     // per tile and pixel row: the winding of the pieces it does not list that lie right of it
uint* CurveTileEntries;      // the lists: piece indexes within the item
uint CurveTileCapacity;      // entries CurveTileEntries holds

float2 CurveTilePixel(CurveTileItem item, float2 p)
{
    float3 h = float3(p, 1.0);
    return float2(dot(h, item.ToPixelX.xyz), dot(h, item.ToPixelY.xyz));
}

// Piece k of an item, in framebuffer pixels. The packed pieces are relative to their box's corner.
void CurveTilePiece(CurveTileItem item, uint k, out float2 s, out float2 c, out float2 e)
{
    uint* words = item.Curves;
    float2 corner = float2(asfloat(words[0]), asfloat(words[1]));
    uint at = CurveHeaderWords + k * 6u;
    s = CurveTilePixel(item, corner + float2(asfloat(words[at]), asfloat(words[at + 1u])));
    c = CurveTilePixel(item, corner + float2(asfloat(words[at + 2u]), asfloat(words[at + 3u])));
    e = CurveTilePixel(item, corner + float2(asfloat(words[at + 4u]), asfloat(words[at + 5u])));
}

// How much of the pixel centred at pos - framebuffer pixels - item covers, 0..1. The winding along +x is the tile row's
// backdrop plus the listed pieces, each softened over its pixel; along +y it is that winding, exact at the centre, with
// the listed pieces near the centre softened - only a piece within half a pixel softens, and every such piece is listed.
float CurveTileCoverage(float2 pos, uint itemIndex)
{
    CurveTileItem* items = CurveTileItems;
    CurveTileItem item = items[itemIndex];
    int2 tile = int2(floor(pos / CurveTileSize)) - int2(item.TileX, item.TileY);
    if (tile.x < 0 || tile.y < 0 || tile.x >= int(item.Columns) || tile.y >= int(item.Rows))
    {
        return 0.0;
    }

    uint t = item.TileBase + uint(tile.y) * item.Columns + uint(tile.x);
    int row = int(floor(pos.y)) - (item.TileY + tile.y) * CurveTileHeight;
    int* backdrops = CurveTileBackdrops;
    float hard = float(backdrops[t * uint(CurveTileHeight) + uint(clamp(row, 0, CurveTileHeight - 1))]);
    float softX = hard;
    float softY = 0.0;
    float nearestX = 1e20;
    float nearestY = 1e20;

    uint* counts = CurveTileCounts;
    uint* offsets = CurveTileOffsets;
    uint* entries = CurveTileEntries;
    uint first = offsets[t];
    uint count = counts[t];
    count = first >= CurveTileCapacity ? 0u : min(count, CurveTileCapacity - first);
    for (uint i = 0u; i < count; i++)
    {
        float2 s;
        float2 c;
        float2 e;
        CurveTilePiece(item, entries[first + i], s, c, e);
        if (pos.y >= min(s.y, e.y) && pos.y < max(s.y, e.y))
        {
            float hit = CurveAt(s.x, c.x, e.x, CurveCrossing(s.y, c.y, e.y, pos.y));
            float d = hit - pos.x;
            float turn = e.y > s.y ? 1.0 : -1.0;
            hard += d >= 0.0 ? turn : 0.0;
            softX += turn * saturate(d + 0.5);
            nearestX = min(nearestX, abs(d));
        }

        if (pos.x >= min(s.x, e.x) && pos.x < max(s.x, e.x))
        {
            float hit = CurveAt(s.y, c.y, e.y, CurveCrossing(s.x, c.x, e.x, pos.x));
            float d = hit - pos.y;
            float turn = e.x < s.x ? 1.0 : -1.0;
            softY += turn * (saturate(d + 0.5) - (d >= 0.0 ? 1.0 : 0.0));
            nearestY = min(nearestY, abs(d));
        }
    }

    softY += hard;
    float coverX = CurveWindingCoverage(softX, item.EvenOdd);
    float coverY = CurveWindingCoverage(softY, item.EvenOdd);
    float weightX = saturate(1.0 - nearestX);
    float weightY = saturate(1.0 - nearestY);
    float weights = weightX + weightY;
    return weights > 1e-4 ? (coverX * weightX + coverY * weightY) / weights : coverX;
}

// The coverage of a curve fill's fragment: local is where it is in the fill's local space, pos in framebuffer pixels,
// instance its instance within the draw. The choice is uniform over the draw, so the walk's derivatives stay valid.
float CurveCoverage(float2 local, float2 pos, uint instance)
{
    if (CurveTiled != 0u)
    {
        return CurveTileCoverage(pos, CurveTileItemBase + instance);
    }

    return CurveFillCoverage(local);
}
