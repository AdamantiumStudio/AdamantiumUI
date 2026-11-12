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
        var pts = mesh?.Points;
        if (pts == null || pts.Length < 3) return null;
        var idx = mesh.HasIndices ? mesh.Indices : null;
        var triCount = idx != null ? idx.Length / 3 : pts.Length / 3;
        if (triCount == 0) return null;

        // Count each side by its rounded corners; keep a real position per rounded corner.
        var sideCount = new Dictionary<TriangleSide, int>();
        var real = new Dictionary<Vector2<double>, Vector2<double>>();

        void AddSide(Vector3<double> a, Vector3<double> b)
        {
            var pa = new Vector2<double>(a.X, a.Y);
            var pb = new Vector2<double>(b.X, b.Y);
            var ra = MathHelper.Coordinates.Round(pa);
            var rb = MathHelper.Coordinates.Round(pb);
            if (ra == rb) return;
            real[ra] = pa;
            real[rb] = pb;
            var side = new TriangleSide(ra, rb);
            sideCount.TryGetValue(side, out var c);
            sideCount[side] = c + 1;
        }

        for (var t = 0; t < triCount; t++)
        {
            Vector3<double> a, b, c;
            if (idx != null) { a = pts[idx[t * 3]]; b = pts[idx[t * 3 + 1]]; c = pts[idx[t * 3 + 2]]; }
            else { a = pts[t * 3]; b = pts[t * 3 + 1]; c = pts[t * 3 + 2]; }
            AddSide(a, b); AddSide(b, c); AddSide(c, a);
        }

        // Boundary sides belong to exactly one triangle -> adjacency of the boundary graph.
        var adj = new Dictionary<Vector2<double>, List<Vector2<double>>>();
        foreach (var kv in sideCount)
        {
            if (kv.Value != 1) continue;
            var p = kv.Key.Low;
            var q = kv.Key.High;
            (adj.TryGetValue(p, out var lp) ? lp : adj[p] = new List<Vector2<double>>()).Add(q);
            (adj.TryGetValue(q, out var lq) ? lq : adj[q] = new List<Vector2<double>>()).Add(p);
        }
        if (adj.Count == 0) return null;

        // Walk closed loops, greedily consuming boundary sides (a >2-degree corner at a self-intersection just picks any
        // unused side - fine for feathering). Each loop = outer outline or one hole.
        var loops = new List<(Vector2<double>[], bool)>();
        var visited = new HashSet<TriangleSide>();
        foreach (var startNode in adj.Keys)
        {
            foreach (var first in adj[startNode])
            {
                if (visited.Contains(new TriangleSide(startNode, first))) continue;

                var loop = new List<Vector2<double>> { real[startNode] };
                var cur = startNode;
                var next = first;
                while (true)
                {
                    if (!visited.Add(new TriangleSide(cur, next))) break;
                    if (next == startNode) break;   // closed - don't re-add the start point
                    loop.Add(real[next]);
                    var prev = cur;
                    cur = next;
                    var found = false;
                    foreach (var nb in adj[cur])
                    {
                        if (nb == prev) continue;
                        if (visited.Contains(new TriangleSide(cur, nb))) continue;
                        next = nb; found = true; break;
                    }
                    if (!found) break;
                }
                if (loop.Count >= 3) loops.Add((loop.ToArray(), true));
            }
        }
        return loops.Count > 0 ? loops : null;
    }
}
