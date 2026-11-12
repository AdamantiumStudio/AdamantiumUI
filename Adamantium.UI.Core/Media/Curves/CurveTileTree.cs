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
    private const int MostPieces = 8;
    private const double LargeTileDivisions = 16;
    private const int EntriesPerPiece = 64;
    private const int DeepestLevel = 20;
    private const double Clearance = 1e-6;
    private const int WordsPerPiece = 6;

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
    /// <paramref name="corner"/> of <paramref name="size"/>. A tile is cut while it lists more than a few pieces, or
    /// two or more while it is large, and the lists stay within a budget per piece; it is cut breadth first, so the
    /// budget goes to the coarse tiles every zoom reads.</summary>
    public static CurveTileTree Of(QuadraticCurve[] pieces, Vector2<double> corner, Vector2<double> size)
    {
        var coordinates = CoordinatesOf(pieces, corner);
        var allPieces = new List<int>(pieces.Length);
        for (var piece = 0; piece < pieces.Length; piece++)
        {
            allPieces.Add(piece);
        }

        var rootSide = Math.Max(Math.Max(size.X, size.Y), 1e-30);
        var lefts = new List<double> { 0 };
        var tops = new List<double> { 0 };
        var levels = new List<int> { 0 };
        var lists = new List<List<int>> { allPieces };
        var pointsX = new List<double>();
        var pointsY = new List<double>();
        var windings = new List<int>();
        var firstChildren = new List<int> { 0 };

        var clearance = Clearance * rootSide;
        var rootPoint = PointOf(coordinates, allPieces, 0, 0, rootSide, double.NaN, clearance);
        pointsX.Add(rootPoint.X);
        pointsY.Add(rootPoint.Y);
        windings.Add(WindingAlongX(coordinates, allPieces, rootPoint.X, rootPoint.Y));

        var budget = (long)EntriesPerPiece * Math.Max(1, pieces.Length);
        long spent = allPieces.Count;
        var quarterLists = new List<int>[4];
        var largeSide = rootSide / LargeTileDivisions;
        for (var tile = 0; tile < lists.Count; tile++)
        {
            var list = lists[tile];
            var tileSide = rootSide / (1L << levels[tile]);
            var crowded = list.Count > MostPieces || (list.Count > 1 && tileSide > largeSide);
            if (!crowded || levels[tile] >= DeepestLevel)
            {
                continue;
            }

            var half = tileSide * 0.5;
            long cost = 0;
            for (var quarter = 0; quarter < 4; quarter++)
            {
                quarterLists[quarter] = Near(coordinates, list, lefts[tile] + (quarter & 1) * half,
                    tops[tile] + (quarter >> 1) * half, half);
                cost += quarterLists[quarter].Count;
            }

            if (spent + cost > budget)
            {
                continue;
            }

            spent += cost;
            firstChildren[tile] = lists.Count;
            for (var quarter = 0; quarter < 4; quarter++)
            {
                var left = lefts[tile] + (quarter & 1) * half;
                var top = tops[tile] + (quarter >> 1) * half;
                var point = PointOf(coordinates, list, left, top, half, pointsY[tile], clearance);
                lefts.Add(left);
                tops.Add(top);
                levels.Add(levels[tile] + 1);
                lists.Add(quarterLists[quarter]);
                pointsX.Add(point.X);
                pointsY.Add(point.Y);
                windings.Add(windings[tile] + Across(coordinates, list, pointsX[tile], pointsY[tile], point.X, point.Y));
                firstChildren.Add(0);
            }
        }

        var tileCount = lists.Count;
        var points = new float[tileCount * 2];
        var listStarts = new int[tileCount + 1];
        var entries = new int[spent];
        var written = 0;
        for (var tile = 0; tile < tileCount; tile++)
        {
            points[tile * 2] = (float)pointsX[tile];
            points[tile * 2 + 1] = (float)pointsY[tile];
            listStarts[tile] = written;
            lists[tile].CopyTo(entries, written);
            written += lists[tile].Count;
        }

        listStarts[tileCount] = written;
        return new CurveTileTree(rootSide, points, windings.ToArray(), firstChildren.ToArray(), listStarts, entries);
    }

    private static double[] CoordinatesOf(QuadraticCurve[] pieces, Vector2<double> corner)
    {
        var coordinates = new double[pieces.Length * WordsPerPiece];
        for (var piece = 0; piece < pieces.Length; piece++)
        {
            var at = piece * WordsPerPiece;
            coordinates[at] = (float)(pieces[piece].Start.X - corner.X);
            coordinates[at + 1] = (float)(pieces[piece].Start.Y - corner.Y);
            coordinates[at + 2] = (float)(pieces[piece].Control.X - corner.X);
            coordinates[at + 3] = (float)(pieces[piece].Control.Y - corner.Y);
            coordinates[at + 4] = (float)(pieces[piece].End.X - corner.X);
            coordinates[at + 5] = (float)(pieces[piece].End.Y - corner.Y);
        }

        return coordinates;
    }

    private static List<int> Near(double[] coordinates, List<int> list, double left, double top, double side)
    {
        var margin = side * 0.5;
        var boxLeft = left - margin;
        var boxRight = left + side + margin;
        var boxTop = top - margin;
        var boxBottom = top + side + margin;
        var near = new List<int>();
        foreach (var piece in list)
        {
            var at = piece * WordsPerPiece;
            double startX = coordinates[at], startY = coordinates[at + 1];
            double endX = coordinates[at + 4], endY = coordinates[at + 5];
            var lowY = Math.Min(startY, endY);
            var highY = Math.Max(startY, endY);
            if (highY < boxTop || lowY > boxBottom || Math.Max(startX, endX) < boxLeft || Math.Min(startX, endX) > boxRight)
            {
                continue;
            }

            if (lowY >= boxTop && highY <= boxBottom)
            {
                near.Add(piece);
                continue;
            }

            var xAtTop = XAt(coordinates, at, Math.Clamp(boxTop, lowY, highY));
            var xAtBottom = XAt(coordinates, at, Math.Clamp(boxBottom, lowY, highY));
            if (Math.Max(xAtTop, xAtBottom) >= boxLeft && Math.Min(xAtTop, xAtBottom) <= boxRight)
            {
                near.Add(piece);
            }
        }

        return near;
    }

    private static Vector2<double> PointOf(double[] coordinates, List<int> list, double left, double top, double side,
        double parentY, double clearance)
    {
        var best = new Vector2<double>((float)(left + side * 0.5), (float)(top + side * 0.5));
        var bestGap = double.MinValue;
        foreach (var offsetY in Offsets)
        {
            var y = (double)(float)(top + side * offsetY);
            foreach (var offsetX in Offsets)
            {
                var x = (double)(float)(left + side * offsetX);
                var gap = Gap(coordinates, list, x, y, parentY, left - side * 0.5, left + side * 1.5);
                if (gap >= clearance)
                {
                    return new Vector2<double>(x, y);
                }

                if (gap > bestGap)
                {
                    bestGap = gap;
                    best = new Vector2<double>(x, y);
                }
            }
        }

        return best;
    }

    private static double Gap(double[] coordinates, List<int> list, double x, double y, double parentY, double tileLeft,
        double tileRight)
    {
        var fromParent = !double.IsNaN(parentY);
        var legLow = fromParent ? Math.Min(parentY, y) : y;
        var legHigh = fromParent ? Math.Max(parentY, y) : y;
        var gap = double.MaxValue;
        foreach (var piece in list)
        {
            var at = piece * WordsPerPiece;
            double startX = coordinates[at], startY = coordinates[at + 1];
            double endX = coordinates[at + 4], endY = coordinates[at + 5];
            if (Math.Max(startX, endX) >= tileLeft && Math.Min(startX, endX) <= tileRight)
            {
                gap = Math.Min(gap, Math.Min(Math.Abs(startY - y), Math.Abs(endY - y)));
            }

            if (fromParent && Math.Max(startY, endY) >= legLow && Math.Min(startY, endY) <= legHigh)
            {
                gap = Math.Min(gap, Math.Min(Math.Abs(startX - x), Math.Abs(endX - x)));
            }

            if (Crosses(startY, endY, y))
            {
                gap = Math.Min(gap, Math.Abs(XAt(coordinates, at, y) - x));
            }

            if (Crosses(startX, endX, x))
            {
                var hitY = YAt(coordinates, at, x);
                gap = Math.Min(gap, Math.Abs(hitY - y));
                if (fromParent)
                {
                    gap = Math.Min(gap, Math.Abs(hitY - parentY));
                }
            }

            if (fromParent && Crosses(startY, endY, parentY))
            {
                gap = Math.Min(gap, Math.Abs(XAt(coordinates, at, parentY) - x));
            }
        }

        return gap;
    }

    private static int WindingAlongX(double[] coordinates, List<int> list, double x, double y)
    {
        var winding = 0;
        foreach (var piece in list)
        {
            var at = piece * WordsPerPiece;
            double startY = coordinates[at + 1], endY = coordinates[at + 5];
            if (Crosses(startY, endY, y) && XAt(coordinates, at, y) > x)
            {
                winding += endY > startY ? 1 : -1;
            }
        }

        return winding;
    }

    private static int Across(double[] coordinates, List<int> list, double fromX, double fromY, double toX, double toY)
    {
        var change = 0;
        foreach (var piece in list)
        {
            var at = piece * WordsPerPiece;
            double startX = coordinates[at], startY = coordinates[at + 1];
            double endX = coordinates[at + 4], endY = coordinates[at + 5];
            if (Crosses(startY, endY, fromY))
            {
                var hitX = XAt(coordinates, at, fromY);
                var turnX = endY > startY ? 1 : -1;
                if (toX > fromX && hitX > fromX && hitX <= toX) change -= turnX;
                if (toX < fromX && hitX > toX && hitX <= fromX) change += turnX;
            }

            if (Crosses(startX, endX, toX))
            {
                var hitY = YAt(coordinates, at, toX);
                var turnY = endX < startX ? 1 : -1;
                if (toY > fromY && hitY > fromY && hitY <= toY) change -= turnY;
                if (toY < fromY && hitY > toY && hitY <= fromY) change += turnY;
            }
        }

        return change;
    }

    private static bool Crosses(double from, double to, double value) =>
        value >= Math.Min(from, to) && value < Math.Max(from, to);

    private static double XAt(double[] coordinates, int at, double y) =>
        PointAlong(coordinates[at], coordinates[at + 2], coordinates[at + 4],
            CurvePieces.Crossing(coordinates[at + 1], coordinates[at + 3], coordinates[at + 5], y));

    private static double YAt(double[] coordinates, int at, double x) =>
        PointAlong(coordinates[at + 1], coordinates[at + 3], coordinates[at + 5],
            CurvePieces.Crossing(coordinates[at], coordinates[at + 2], coordinates[at + 4], x));

    private static double PointAlong(double start, double control, double end, double t)
    {
        var rest = 1 - t;
        return start * rest * rest + control * 2 * rest * t + end * t * t;
    }
}
