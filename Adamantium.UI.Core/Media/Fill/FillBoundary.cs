using System.Collections.Generic;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Media.Fill;

// Extracts the painted outline of a tessellated fill as closed loops (edges owned by a single triangle), so the AA
// fringe also feathers holes and self-intersections.
internal static class FillBoundary
{
    /// <summary>The edge of <paramref name="geometry"/>'s fill: its contours' own points when the geometry says they are
    /// exactly that edge - a mesh never changes a contour's points once made, it makes new contours - else, or when
    /// none of them is a ring after all, worked out of its triangles by <see cref="ExtractLoops"/>.</summary>
    public static List<(Vector2<double>[] Points, bool IsClosed)> LoopsOf(Geometry geometry)
    {
        if (geometry is not { ContoursAreFillBoundary: true })
        {
            return ExtractLoops(geometry?.Mesh);
        }

        var loops = new List<(Vector2<double>[] Points, bool IsClosed)>();
        foreach (var contour in geometry.Mesh.Contours)
        {
            if (contour.IsGeometryClosed && contour.Points is { Length: >= 3 } points)
            {
                loops.Add((points, true));
            }
        }

        return loops.Count > 0 ? loops : ExtractLoops(geometry.Mesh);
    }

    /// <summary>The fill's boundary loops, or null if the mesh has no drawable triangles (caller falls back to the raw
    /// path contours). Each loop is a closed point list (point[n-1] -> point[0] implied), in mesh-LOCAL coordinates.</summary>
    public static List<(Vector2<double>[] Points, bool IsClosed)> ExtractLoops(Mesh mesh)
    {
        var points = mesh?.Points;
        if (points == null || points.Length < 3)
        {
            return null;
        }

        var indices = mesh.HasIndices ? mesh.Indices : null;
        var triangleCount = indices != null ? indices.Length / 3 : points.Length / 3;
        if (triangleCount == 0)
        {
            return null;
        }

        var sideCount = new Dictionary<TriangleSide, int>();
        var real = new Dictionary<Vector2<double>, Vector2<double>>();
        for (var t = 0; t < triangleCount; t++)
        {
            var first = indices != null ? indices[t * 3] : t * 3;
            var second = indices != null ? indices[t * 3 + 1] : t * 3 + 1;
            var third = indices != null ? indices[t * 3 + 2] : t * 3 + 2;
            CountSide(points[first], points[second], sideCount, real);
            CountSide(points[second], points[third], sideCount, real);
            CountSide(points[third], points[first], sideCount, real);
        }

        var adjacent = BoundaryAdjacency(sideCount);
        if (adjacent.Count == 0)
        {
            return null;
        }

        var loops = WalkLoops(adjacent, real);
        return loops.Count > 0 ? loops : null;
    }

    private static void CountSide(Vector3<double> a, Vector3<double> b, Dictionary<TriangleSide, int> sideCount,
        Dictionary<Vector2<double>, Vector2<double>> real)
    {
        var pa = new Vector2<double>(a.X, a.Y);
        var pb = new Vector2<double>(b.X, b.Y);
        var ra = MathHelper.Coordinates.Round(pa);
        var rb = MathHelper.Coordinates.Round(pb);
        if (ra == rb)
        {
            return;
        }

        real[ra] = pa;
        real[rb] = pb;
        var side = new TriangleSide(ra, rb);
        sideCount.TryGetValue(side, out var count);
        sideCount[side] = count + 1;
    }

    private static Dictionary<Vector2<double>, List<Vector2<double>>> BoundaryAdjacency(Dictionary<TriangleSide, int> sideCount)
    {
        var adjacent = new Dictionary<Vector2<double>, List<Vector2<double>>>();
        foreach (var (side, count) in sideCount)
        {
            if (count != 1)
            {
                continue;
            }

            Neighbours(adjacent, side.Low).Add(side.High);
            Neighbours(adjacent, side.High).Add(side.Low);
        }

        return adjacent;
    }

    private static List<Vector2<double>> Neighbours(Dictionary<Vector2<double>, List<Vector2<double>>> adjacent,
        Vector2<double> corner)
    {
        if (!adjacent.TryGetValue(corner, out var neighbours))
        {
            neighbours = new List<Vector2<double>>();
            adjacent[corner] = neighbours;
        }

        return neighbours;
    }

    private static List<(Vector2<double>[] Points, bool IsClosed)> WalkLoops(
        Dictionary<Vector2<double>, List<Vector2<double>>> adjacent, Dictionary<Vector2<double>, Vector2<double>> real)
    {
        var loops = new List<(Vector2<double>[] Points, bool IsClosed)>();
        var visited = new HashSet<TriangleSide>();
        foreach (var start in adjacent.Keys)
        {
            foreach (var first in adjacent[start])
            {
                if (visited.Contains(new TriangleSide(start, first)))
                {
                    continue;
                }

                var loop = WalkLoop(start, first, adjacent, real, visited);
                if (loop.Count >= 3)
                {
                    loops.Add((loop.ToArray(), true));
                }
            }
        }

        return loops;
    }

    private static List<Vector2<double>> WalkLoop(Vector2<double> start, Vector2<double> first,
        Dictionary<Vector2<double>, List<Vector2<double>>> adjacent, Dictionary<Vector2<double>, Vector2<double>> real,
        HashSet<TriangleSide> visited)
    {
        var loop = new List<Vector2<double>> { real[start] };
        var current = start;
        var next = first;
        while (visited.Add(new TriangleSide(current, next)) && next != start)
        {
            loop.Add(real[next]);
            var previous = current;
            current = next;
            if (!TryNextSide(current, previous, adjacent, visited, out next))
            {
                break;
            }
        }

        return loop;
    }

    private static bool TryNextSide(Vector2<double> current, Vector2<double> previous,
        Dictionary<Vector2<double>, List<Vector2<double>>> adjacent, HashSet<TriangleSide> visited, out Vector2<double> next)
    {
        foreach (var neighbour in adjacent[current])
        {
            if (neighbour != previous && !visited.Contains(new TriangleSide(current, neighbour)))
            {
                next = neighbour;
                return true;
            }
        }

        next = default;
        return false;
    }
}
