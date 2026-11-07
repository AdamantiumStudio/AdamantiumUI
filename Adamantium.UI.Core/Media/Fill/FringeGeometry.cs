using System;
using System.Collections.Generic;
using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Media.Fill;

// The AA fringe geometry shared by the per-unit and instanced paths: each contour's feather winding and its triangle
// ring. The ring stores edge directions, not width; the vertex shader builds a 1px miter, so rings are scale-free.
internal static class FringeGeometry
{
    private const int MaxNestingGridSide = 256;
    private const int LargeBoxShare = 4;
    private const int MostCellsPerContour = 16;

    /// <summary>Each drawable contour (>= 3 points) with the sign that makes its fringe feather AWAY from the fill.</summary>
    public static List<(Vector2<double>[] Points, float Winding)> Build(IReadOnlyList<(Vector2<double>[] Points, bool IsClosed)> contours)
    {
        List<(Vector2<double>[] Points, float Winding)> built = [];
        foreach (var (points, _) in contours)
        {
            if (points is not { Length: >= 3 }) continue;   // a fill contour needs at least a triangle
            // Outward miter sign from the contour's signed area (screen space is y-down, so the sign is tuned by the
            // headless edge test: the fringe must land OUTSIDE the shape).
            built.Add((points, SignedArea(points) >= 0 ? -1f : 1f));
        }
        ApplyNesting(built);
        return built;
    }

    /// <summary>The ring for one mesh's contours as ONE list of edges (contours back to back), so a shared ring is a single
    /// draw of six corners per edge. An edge lying along a side of <paramref name="cut"/> is where the fill was cut, not
    /// where it ends, and gets no ring.</summary>
    public static RingEdge[] BuildRing(List<(Vector2<double>[] Points, float Winding)> contours, Rect? cut = null)
    {
        // Counted first, so the ring is one array of its own size: a tile's areas have edges by the hundred thousand.
        var count = 0;
        foreach (var (points, _) in contours)
        {
            var n = points.Length;
            for (var i = 0; i < n; i++)
            {
                if (cut is not { } box || !AlongASide(points[i], points[(i + 1) % n], box))
                {
                    count++;
                }
            }
        }

        var edges = new RingEdge[count];
        var at = 0;
        foreach (var (points, winding) in contours)
        {
            var n = points.Length;
            for (var i = 0; i < n; i++)
            {
                var ni = (i + 1) % n;
                if (cut is { } box && AlongASide(points[i], points[ni], box))
                {
                    continue;
                }

                // Winding folded into the directions' sign: reversing a direction reverses its 90-degree normal, which is
                // how a hole feathers inward.
                edges[at++] = new RingEdge
                {
                    A = new Vector2F((float)points[i].X, (float)points[i].Y),
                    B = new Vector2F((float)points[ni].X, (float)points[ni].Y),
                    InDir = SafeDir(points[i] - points[(i + n - 1) % n]) * winding,
                    EdgeDir = SafeDir(points[ni] - points[i]) * winding,
                    OutDir = SafeDir(points[(ni + 1) % n] - points[ni]) * winding
                };
            }
        }

        return edges;
    }

    private static bool AlongASide(Vector2<double> a, Vector2<double> b, Rect box)
    {
        var tolerance = 1e-6 * Math.Max(1, Math.Max(box.Width, box.Height));
        bool On(double value, double side) => Math.Abs(value - side) <= tolerance;
        return On(a.X, box.X) && On(b.X, box.X) || On(a.X, box.Right) && On(b.X, box.Right)
            || On(a.Y, box.Y) && On(b.Y, box.Y) || On(a.Y, box.Bottom) && On(b.Y, box.Bottom);
    }

    private static Vector2F SafeDir(Vector2<double> v)
    {
        var len = Math.Sqrt(v.X * v.X + v.Y * v.Y);
        if (len <= 1e-9) return Vector2F.Zero;   // duplicate contour points would otherwise produce a NaN direction
        return new Vector2F((float)(v.X / len), (float)(v.Y / len));
    }

    // Even-odd nesting: a contour inside an ODD number of the others is a HOLE - the fill is OUTSIDE it, so its fringe
    // must feather INWARD (toward the hole), opposite an outer contour. Winding alone can't tell them apart (the
    // tessellator emits holes with the same winding as outers), so flip holes here. A probe VERTEX is used, not the
    // centroid - a frame-shaped outer's centroid can fall inside its own hole and misclassify it.
    //
    // A fill may have thousands of contours - a map tile's buildings - so each contour's box goes into the cells of a grid
    // it covers, and a probe asks only the contours of its own cell and the few large ones.
    private static void ApplyNesting(List<(Vector2<double>[] Points, float Winding)> built)
    {
        var count = built.Count;
        if (count < 2) return;

        var boxes = new (double MinX, double MinY, double MaxX, double MaxY)[count];
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        for (var j = 0; j < count; j++)
        {
            var box = boxes[j] = BoxOf(built[j].Points);
            minX = Math.Min(minX, box.MinX);
            minY = Math.Min(minY, box.MinY);
            maxX = Math.Max(maxX, box.MaxX);
            maxY = Math.Max(maxY, box.MaxY);
        }

        bool Holds(int j, int i, Vector2<double> probe)
        {
            var box = boxes[j];
            return j != i && probe.X >= box.MinX && probe.X <= box.MaxX && probe.Y >= box.MinY && probe.Y <= box.MaxY &&
                   PointInPolygon(probe, built[j].Points);
        }

        // Along an axis with no extent - every contour on one line - or none that can be divided, that axis is one cell.
        var side = Math.Clamp((int)Math.Sqrt(count), 1, MaxNestingGridSide);
        var columns = maxX - minX is var width && width > 0 && double.IsFinite(width) ? side : 1;
        var rows = maxY - minY is var height && height > 0 && double.IsFinite(height) ? side : 1;
        var cellWidth = columns > 1 ? width / columns : 1;
        var cellHeight = rows > 1 ? height / rows : 1;
        int Column(double x) => columns == 1 ? 0 : Math.Clamp((int)((x - minX) / cellWidth), 0, columns - 1);
        int Row(double y) => rows == 1 ? 0 : Math.Clamp((int)((y - minY) / cellHeight), 0, rows - 1);

        // Each box's cells, worked out once. One over a quarter of the cells or more - a tile's ground, a frame round
        // everything - is asked by every probe rather than written into all those cells.
        var cells = columns * rows;
        var spans = new (int Column0, int Column1, int Row0, int Row1)[count];
        var large = new List<int>();
        var starts = new int[cells + 1];
        for (var j = 0; j < count; j++)
        {
            var box = boxes[j];
            var span = spans[j] = (Column(box.MinX), Column(box.MaxX), Row(box.MinY), Row(box.MaxY));
            var covered = Math.Max(0, span.Column1 - span.Column0 + 1) * Math.Max(0, span.Row1 - span.Row0 + 1);
            if (covered * LargeBoxShare >= cells)
            {
                large.Add(j);
                spans[j] = (0, -1, 0, -1);
                continue;
            }

            for (var row = span.Row0; row <= span.Row1; row++)
            for (var column = span.Column0; column <= span.Column1; column++)
                starts[row * columns + column + 1]++;
        }

        for (var cell = 0; cell < cells; cell++) starts[cell + 1] += starts[cell];

        // Many middling boxes - strips each over a fifth of the tile - would fill the grid as full as asking every box;
        // then it is not built at all.
        if (starts[cells] > MostCellsPerContour * count)
        {
            for (var i = 0; i < count; i++)
            {
                var probe = built[i].Points[0];
                var nesting = 0;
                for (var j = 0; j < count; j++)
                {
                    if (Holds(j, i, probe)) nesting++;
                }

                if ((nesting & 1) == 1) built[i] = (built[i].Points, -built[i].Winding);
            }

            return;
        }

        // The cells' contours back to back, laid out by the running totals above.
        var members = new int[starts[cells]];
        var filled = new int[cells];
        for (var j = 0; j < count; j++)
        {
            var span = spans[j];
            for (var row = span.Row0; row <= span.Row1; row++)
            for (var column = span.Column0; column <= span.Column1; column++)
            {
                var cell = row * columns + column;
                members[starts[cell] + filled[cell]++] = j;
            }
        }

        for (var i = 0; i < count; i++)
        {
            var probe = built[i].Points[0];
            var cell = Row(probe.Y) * columns + Column(probe.X);
            var nesting = 0;
            for (var k = starts[cell]; k < starts[cell + 1]; k++)
            {
                if (Holds(members[k], i, probe)) nesting++;
            }

            foreach (var j in large)
            {
                if (Holds(j, i, probe)) nesting++;
            }

            if ((nesting & 1) == 1) built[i] = (built[i].Points, -built[i].Winding);
        }
    }

    private static (double MinX, double MinY, double MaxX, double MaxY) BoxOf(Vector2<double>[] points)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var p in points)
        {
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
        }
        return (minX, minY, maxX, maxY);
    }

    // Ray-cast point-in-polygon (used to find a contour's even-odd nesting depth -> hole vs outer).
    private static bool PointInPolygon(Vector2<double> p, Vector2<double>[] poly)
    {
        var inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) &&
                p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                inside = !inside;
        }
        return inside;
    }

    private static double SignedArea(Vector2<double>[] p)
    {
        double a = 0;
        for (int i = 0, n = p.Length; i < n; i++)
        {
            var j = (i + 1) % n;
            a += p[i].X * p[j].Y - p[j].X * p[i].Y;
        }
        return a * 0.5;
    }
}
