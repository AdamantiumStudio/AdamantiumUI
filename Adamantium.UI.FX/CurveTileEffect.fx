// CURVE TILE BINNING - the compute passes that ready a flush's curve fills for CurveTileCoverage (see CurveTiles.fxh):
// Clear, Count (lists sized, backdrops gathered), Allocate (lists placed), Backdrop (summed along each row) and Fill.
// One thread per piece bins it row by row of tiles, so the work follows the pieces and the tiles they cross.

// In dependency order. NOTHING after the path on these lines - see BatchEffect.fx.
#include "Includes/CommonData.fxh"
#include "Includes/ClipMath.fxh"
#include "Includes/ShapeMath.fxh"
#include "Includes/CurveMath.fxh"
#include "Includes/CurveTiles.fxh"

uint* CurveTileCursors;      // per tile: entries written so far by Fill
uint* CurveTileCounter;      // one uint: entries placed so far by Allocate
uint CurveTileItemCount;
uint CurveTilePieceTotal;
uint CurveTileTotal;         // tiles of all items
uint CurveTileRowTotal;      // tile rows of all items

// The item whose pieces - or tile rows, byRows - hold index g: the last whose first is at or before it.
uint CurveTileItemOf(uint g, bool byRows)
{
    CurveTileItem* items = CurveTileItems;
    uint lo = 0u;
    uint hi = CurveTileItemCount - 1u;
    while (lo < hi)
    {
        uint mid = (lo + hi + 1u) / 2u;
        uint first = byRows ? items[mid].RowBase : items[mid].PieceFirst;
        if (first <= g)
        {
            lo = mid;
        }
        else
        {
            hi = mid - 1u;
        }
    }

    return lo;
}

// Bins piece k of an item, row of tiles by row: in each, the columns it passes within the margin of. Counting, it also
// adds its turn to the backdrop of the tile just left of those columns, for each pixel row it spans; filling, it writes
// itself into those columns' lists. Both passes run this same code, so they agree on which tiles list it.
void CurveTileBin(CurveTileItem item, uint k, float2 s, float2 c, float2 e, bool fill)
{
    float ylo = min(s.y, e.y);
    float yhi = max(s.y, e.y);
    int r0 = max(int(floor((ylo - CurveTileMargin) / CurveTileSize.y)) - item.TileY, 0);
    int r1 = min(int(floor((yhi + CurveTileMargin) / CurveTileSize.y)) - item.TileY, int(item.Rows) - 1);
    int columns = int(item.Columns);
    int turn = e.y > s.y ? 1 : -1;
    uint* counts = CurveTileCounts;
    uint* cursors = CurveTileCursors;
    uint* offsets = CurveTileOffsets;
    uint* entries = CurveTileEntries;
    int* backdrops = CurveTileBackdrops;
    for (int r = r0; r <= r1; r++)
    {
        int top = (item.TileY + r) * CurveTileHeight;
        float xa = s.x;
        float xb = e.x;
        if (yhi > ylo)
        {
            float bandTop = clamp(float(top) - CurveTileMargin, ylo, yhi);
            float bandBottom = clamp(float(top) + CurveTileSize.y + CurveTileMargin, ylo, yhi);
            xa = CurveAt(s.x, c.x, e.x, CurveCrossing(s.y, c.y, e.y, bandTop));
            xb = CurveAt(s.x, c.x, e.x, CurveCrossing(s.y, c.y, e.y, bandBottom));
        }

        int c0 = int(floor((min(xa, xb) - CurveTileMargin) / CurveTileSize.x)) - item.TileX;
        int c1 = int(floor((max(xa, xb) + CurveTileMargin) / CurveTileSize.x)) - item.TileX;
        uint rowTile = item.TileBase + uint(r) * item.Columns;

        if (!fill && c0 > 0 && yhi > ylo)
        {
            // The pixel rows of this tile row whose centres it spans: ylo <= j + 0.5 < yhi.
            uint left = rowTile + uint(min(c0, columns) - 1);
            int j0 = max(int(ceil(ylo - 0.5)), top);
            int j1 = min(int(ceil(yhi - 0.5)) - 1, top + CurveTileHeight - 1);
            for (int j = j0; j <= j1; j++)
            {
                InterlockedAdd(backdrops[left * uint(CurveTileHeight) + uint(j - top)], turn);
            }
        }

        int from = max(c0, 0);
        int to = min(c1, columns - 1);
        for (int col = from; col <= to; col++)
        {
            uint tile = rowTile + uint(col);
            if (!fill)
            {
                InterlockedAdd(counts[tile], 1u);
            }
            else
            {
                uint slot;
                InterlockedAdd(cursors[tile], 1u, slot);
                uint at = offsets[tile] + slot;
                if (slot < counts[tile] && at < CurveTileCapacity)
                {
                    entries[at] = k;
                }
            }
        }
    }
}

void CurveTileBinPiece(uint g, bool fill)
{
    CurveTileItem* items = CurveTileItems;
    CurveTileItem item = items[CurveTileItemOf(g, false)];
    uint k = g - item.PieceFirst;
    float2 s;
    float2 c;
    float2 e;
    CurveTilePiece(item, k, s, c, e);
    CurveTileBin(item, k, s, c, e, fill);
}

[shader("compute")]
[numthreads(64, 1, 1)]
void CurveTileClearCS(uint3 id : SV_DispatchThreadID)
{
    uint t = id.x;
    if (t == 0u)
    {
        uint* counter = CurveTileCounter;
        counter[0] = 0u;
    }

    if (t >= CurveTileTotal)
    {
        return;
    }

    uint* counts = CurveTileCounts;
    uint* cursors = CurveTileCursors;
    int* backdrops = CurveTileBackdrops;
    counts[t] = 0u;
    cursors[t] = 0u;
    for (uint k = 0u; k < uint(CurveTileHeight); k++)
    {
        backdrops[t * uint(CurveTileHeight) + k] = 0;
    }
}

[shader("compute")]
[numthreads(64, 1, 1)]
void CurveTileCountCS(uint3 id : SV_DispatchThreadID)
{
    if (id.x < CurveTilePieceTotal)
    {
        CurveTileBinPiece(id.x, false);
    }
}

[shader("compute")]
[numthreads(64, 1, 1)]
void CurveTileAllocateCS(uint3 id : SV_DispatchThreadID)
{
    uint t = id.x;
    if (t >= CurveTileTotal)
    {
        return;
    }

    uint* counts = CurveTileCounts;
    uint* offsets = CurveTileOffsets;
    uint* counter = CurveTileCounter;
    uint at;
    InterlockedAdd(counter[0], counts[t], at);
    offsets[t] = at;
}

// Each tile row's backdrops, one thread per pixel row, summed from the right: a tile's is then everything right of it.
[shader("compute")]
[numthreads(64, 1, 1)]
void CurveTileBackdropCS(uint3 id : SV_DispatchThreadID)
{
    uint g = id.x;
    if (g >= CurveTileRowTotal * uint(CurveTileHeight))
    {
        return;
    }

    uint row = g / uint(CurveTileHeight);
    uint pixelRow = g % uint(CurveTileHeight);
    CurveTileItem* items = CurveTileItems;
    CurveTileItem item = items[CurveTileItemOf(row, true)];
    uint rowTile = item.TileBase + (row - item.RowBase) * item.Columns;
    int* backdrops = CurveTileBackdrops;
    int sum = 0;
    for (int col = int(item.Columns) - 1; col >= 0; col--)
    {
        uint at = (rowTile + uint(col)) * uint(CurveTileHeight) + pixelRow;
        sum += backdrops[at];
        backdrops[at] = sum;
    }
}

[shader("compute")]
[numthreads(64, 1, 1)]
void CurveTileFillCS(uint3 id : SV_DispatchThreadID)
{
    if (id.x < CurveTilePieceTotal)
    {
        CurveTileBinPiece(id.x, true);
    }
}

technique CurveTile
{
    pass Clear
    {
        ComputeShader = CurveTileClearCS;
    }

    pass Count
    {
        ComputeShader = CurveTileCountCS;
    }

    pass Allocate
    {
        ComputeShader = CurveTileAllocateCS;
    }

    pass Backdrop
    {
        ComputeShader = CurveTileBackdropCS;
    }

    pass Fill
    {
        ComputeShader = CurveTileFillCS;
    }
}
