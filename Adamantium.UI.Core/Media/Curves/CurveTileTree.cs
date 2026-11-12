using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Media.Curves;

/// <summary>
/// A fill's pieces in a tree of square tiles of its own space: the root covers the pieces' box, and a crowded tile is cut
/// into four, while the memory allowed lasts. Every tile lists the pieces within half its side of it and holds the exact
/// winding at a point of its own. A fragment goes down from the root while the next tile is no smaller than its pixel,
/// and counts its winding from that tile's point along a path inside the tile - so only the tile's pieces can cross it -
/// softening the edges by those within half a pixel, all listed. Made once for the fill's curves: nothing about it
/// depends on where or how large the fill is drawn; empty space inside and out stays in large tiles.
/// </summary>
/// <remarks>Coordinates are the pieces' relative to their box's corner, rounded to float as the shader reads them, so the
/// windings here and the crossings there are counted from the same numbers. A tile's point keeps clear of every piece and
/// of where the path from its parent's point turns, so the two never disagree on which side of a piece it lies.</remarks>
internal sealed class CurveTileTree
{
    // A tile listing more pieces than this is cut, while the budget lasts; one listing fewer, but two or more, is cut while
    // its side is more than the root's over LargeTileDivisions - its pixels would each walk its pieces.
    private const int MostPieces = 8;
    private const double LargeTileDivisions = 16;

    // The list entries all the tiles may make, for a piece.
    private const int EntriesPerPiece = 64;

    // The deepest a tile is cut: its side is the root's halved this many times.
    private const int DeepestLevel = 20;

    // How near, in the root's side, a piece may come to a tile's point or its path before the point is moved: far past
    // what float arithmetic on coordinates of that size can get wrong, far below how near pieces lie.
    private const double Clearance = 1e-6;

    private static readonly double[] Offsets = [0.5, 0.37, 0.63, 0.29, 0.71, 0.43, 0.57, 0.33, 0.67];

    private CurveTileTree(double side, float[] points, int[] windings, int[] children, int[] listStarts, int[] entries)
    {
        Side = side;
        Points = points;
        Windings = windings;
        Children = children;
        ListStarts = listStarts;
        Entries = entries;
    }

    /// <summary>The root tile's side; it lies from the box's corner.</summary>
    public double Side { get; }

    /// <summary>Two to a tile, the root first: where its point is.</summary>
    public float[] Points { get; }

    /// <summary>The winding at each tile's point, counted along a ray toward +x.</summary>
    public int[] Windings { get; }

    /// <summary>Each tile's first child - the four follow each other, left top, right top, left bottom, right bottom -
    /// or 0 for a tile not cut.</summary>
    public int[] Children { get; }

    /// <summary>Where each tile's list starts in <see cref="Entries"/>, and one more: where the last ends.</summary>
    public int[] ListStarts { get; }

    /// <summary>The lists: indexes into the pieces.</summary>
    public int[] Entries { get; }

    public int TileCount => Windings.Length;

    /// <summary>The tree of <paramref name="pieces"/>, each running one way in x and in y, inside the box from
    /// <paramref name="corner"/> of <paramref name="size"/>.</summary>
    public static CurveTileTree Of(QuadraticCurve[] pieces, Vector2<double> corner, Vector2<double> size)
    {
        // Six to a piece - start, control, end - relative to the corner and rounded to float.
        var p = new double[pieces.Length * 6];
        var all = new List<int>(pieces.Length);
        for (var i = 0; i < pieces.Length; i++)
        {
            p[i * 6] = (float)(pieces[i].Start.X - corner.X);
            p[i * 6 + 1] = (float)(pieces[i].Start.Y - corner.Y);
            p[i * 6 + 2] = (float)(pieces[i].Control.X - corner.X);
            p[i * 6 + 3] = (float)(pieces[i].Control.Y - corner.Y);
            p[i * 6 + 4] = (float)(pieces[i].End.X - corner.X);
            p[i * 6 + 5] = (float)(pieces[i].End.Y - corner.Y);
            all.Add(i);
        }

        // The tiles while they are cut, one entry each in these.
        var side = Math.Max(Math.Max(size.X, size.Y), 1e-30);
        var lefts = new List<double> { 0 };
        var tops = new List<double> { 0 };
        var levels = new List<int> { 0 };
        var lists = new List<List<int>> { all };
        var xs = new List<double>();
        var ys = new List<double>();
        var windings = new List<int>();
        var children = new List<int> { 0 };

        var clearance = Clearance * side;
        var (rootX, rootY) = PointOf(p, all, 0, 0, side, double.NaN, clearance);
        xs.Add(rootX);
        ys.Add(rootY);
        windings.Add(WindingAlongX(p, all, rootX, rootY));

        // Cut breadth first, so the budget goes to the coarse tiles every zoom reads before the fine ones.
        var budget = (long)EntriesPerPiece * Math.Max(1, pieces.Length);
        long spent = all.Count;
        var kids = new List<int>[4];
        var large = side / LargeTileDivisions;
        for (var t = 0; t < lists.Count; t++)
        {
            var list = lists[t];
            var tileSide = side / (1L << levels[t]);
            var crowded = list.Count > MostPieces || (list.Count > 1 && tileSide > large);
            if (!crowded || levels[t] >= DeepestLevel)
            {
                continue;
            }

            var half = tileSide * 0.5;
            long cost = 0;
            for (var k = 0; k < 4; k++)
            {
                kids[k] = Near(p, list, lefts[t] + (k & 1) * half, tops[t] + (k >> 1) * half, half);
                cost += kids[k].Count;
            }

            if (spent + cost > budget)
            {
                continue;
            }

            spent += cost;
            children[t] = lists.Count;
            for (var k = 0; k < 4; k++)
            {
                var left = lefts[t] + (k & 1) * half;
                var top = tops[t] + (k >> 1) * half;
                var (x, y) = PointOf(p, list, left, top, half, ys[t], clearance);
                lefts.Add(left);
                tops.Add(top);
                levels.Add(levels[t] + 1);
                lists.Add(kids[k]);
                xs.Add(x);
                ys.Add(y);
                windings.Add(windings[t] + Across(p, list, xs[t], ys[t], x, y));
                children.Add(0);
            }
        }

        var count = lists.Count;
        var points = new float[count * 2];
        var starts = new int[count + 1];
        var entries = new int[spent];
        var at = 0;
        for (var t = 0; t < count; t++)
        {
            points[t * 2] = (float)xs[t];
            points[t * 2 + 1] = (float)ys[t];
            starts[t] = at;
            lists[t].CopyTo(entries, at);
            at += lists[t].Count;
        }

        starts[count] = at;
        return new CurveTileTree(side, points, windings.ToArray(), children.ToArray(), starts, entries);
    }

    // The pieces of a parent's list that pass within half a side of the square from (left, top) of that side.
    private static List<int> Near(double[] p, List<int> list, double left, double top, double side)
    {
        var margin = side * 0.5;
        double x0 = left - margin, x1 = left + side + margin, y0 = top - margin, y1 = top + side + margin;
        var near = new List<int>();
        foreach (var i in list)
        {
            // A piece running one way lies in the box of its ends; only one reaching past the band is narrowed to it.
            var o = i * 6;
            double sx = p[o], sy = p[o + 1], ex = p[o + 4], ey = p[o + 5];
            var low = Math.Min(sy, ey);
            var high = Math.Max(sy, ey);
            if (high < y0 || low > y1 || Math.Max(sx, ex) < x0 || Math.Min(sx, ex) > x1)
            {
                continue;
            }

            if (low >= y0 && high <= y1)
            {
                near.Add(i);
                continue;
            }

            var xa = XAt(p, o, Math.Clamp(y0, low, high));
            var xb = XAt(p, o, Math.Clamp(y1, low, high));
            if (Math.Max(xa, xb) >= x0 && Math.Min(xa, xb) <= x1)
            {
                near.Add(i);
            }
        }

        return near;
    }

    // A tile's point: its middle, or a little off it, where no piece of the list checked comes near deciding a crossing
    // wrongly - along its row or column, or, from the parent's point on the row parentY, where the path turns.
    private static (double X, double Y) PointOf(double[] p, List<int> list, double left, double top, double side,
        double parentY, double clearance)
    {
        var best = ((double)(float)(left + side * 0.5), (double)(float)(top + side * 0.5));
        var bestGap = double.MinValue;
        foreach (var oy in Offsets)
        {
            var y = (double)(float)(top + side * oy);
            foreach (var ox in Offsets)
            {
                var x = (double)(float)(left + side * ox);
                var gap = Gap(p, list, x, y, parentY, left - side * 0.5, left + side * 1.5);
                if (gap >= clearance)
                {
                    return (x, y);
                }

                if (gap > bestGap)
                {
                    bestGap = gap;
                    best = (x, y);
                }
            }
        }

        return best;
    }

    // How near any piece comes to deciding a crossing wrongly at (x, y): its ends to the point's row, where the tile's
    // pixels count from the point between tileLeft and tileRight; its ends to the column the path from the parent's row
    // parentY comes down; its crossings of the row and column to the point, and - from a parent - to the path's corner.
    private static double Gap(double[] p, List<int> list, double x, double y, double parentY, double tileLeft,
        double tileRight)
    {
        var fromParent = !double.IsNaN(parentY);
        var legLow = fromParent ? Math.Min(parentY, y) : y;
        var legHigh = fromParent ? Math.Max(parentY, y) : y;
        var gap = double.MaxValue;
        foreach (var i in list)
        {
            var o = i * 6;
            double sx = p[o], sy = p[o + 1], ex = p[o + 4], ey = p[o + 5];
            if (Math.Max(sx, ex) >= tileLeft && Math.Min(sx, ex) <= tileRight)
            {
                gap = Math.Min(gap, Math.Min(Math.Abs(sy - y), Math.Abs(ey - y)));
            }

            if (fromParent && Math.Max(sy, ey) >= legLow && Math.Min(sy, ey) <= legHigh)
            {
                gap = Math.Min(gap, Math.Min(Math.Abs(sx - x), Math.Abs(ex - x)));
            }

            if (Crosses(sy, ey, y))
            {
                gap = Math.Min(gap, Math.Abs(XAt(p, o, y) - x));
            }

            if (Crosses(sx, ex, x))
            {
                var hit = YAt(p, o, x);
                gap = Math.Min(gap, Math.Abs(hit - y));
                if (fromParent)
                {
                    gap = Math.Min(gap, Math.Abs(hit - parentY));
                }
            }

            if (fromParent && Crosses(sy, ey, parentY))
            {
                gap = Math.Min(gap, Math.Abs(XAt(p, o, parentY) - x));
            }
        }

        return gap;
    }

    // The winding at (x, y) along a ray toward +x: a piece crossed going down counts one, going up minus one.
    private static int WindingAlongX(double[] p, List<int> list, double x, double y)
    {
        var winding = 0;
        foreach (var i in list)
        {
            var o = i * 6;
            if (Crosses(p[o + 1], p[o + 5], y) && XAt(p, o, y) > x)
            {
                winding += p[o + 5] > p[o + 1] ? 1 : -1;
            }
        }

        return winding;
    }

    // How the winding changes from (fromX, fromY) to (toX, toY) along its row, then its column - the path and the rules
    // CurveTileCoverage in CurveTiles.fxh counts by.
    private static int Across(double[] p, List<int> list, double fromX, double fromY, double toX, double toY)
    {
        var change = 0;
        foreach (var i in list)
        {
            var o = i * 6;
            double sx = p[o], sy = p[o + 1], ex = p[o + 4], ey = p[o + 5];
            if (Crosses(sy, ey, fromY))
            {
                var hit = XAt(p, o, fromY);
                var turnX = ey > sy ? 1 : -1;
                if (toX > fromX && hit > fromX && hit <= toX) change -= turnX;
                if (toX < fromX && hit > toX && hit <= fromX) change += turnX;
            }

            if (Crosses(sx, ex, toX))
            {
                var hit = YAt(p, o, toX);
                var turnY = ex < sx ? 1 : -1;
                if (toY > fromY && hit > fromY && hit <= toY) change -= turnY;
                if (toY < fromY && hit > toY && hit <= fromY) change += turnY;
            }
        }

        return change;
    }

    // Whether a piece from a to b in one coordinate crosses v there: v in [low, high).
    private static bool Crosses(double a, double b, double v) => v >= Math.Min(a, b) && v < Math.Max(a, b);

    // Where the piece at o, running one way in y, reaches y - and the same along x.
    private static double XAt(double[] p, int o, double y) =>
        At(p[o], p[o + 2], p[o + 4], CurvePieces.Crossing(p[o + 1], p[o + 3], p[o + 5], y));

    private static double YAt(double[] p, int o, double x) =>
        At(p[o + 1], p[o + 3], p[o + 5], CurvePieces.Crossing(p[o], p[o + 2], p[o + 4], x));

    private static double At(double a, double b, double c, double t)
    {
        var rest = 1 - t;
        return a * rest * rest + b * 2 * rest * t + c * t * t;
    }
}
