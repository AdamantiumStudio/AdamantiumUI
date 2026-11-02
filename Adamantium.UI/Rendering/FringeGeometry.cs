using System;
using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.UI.Rendering.RenderUnits;

namespace Adamantium.UI.Rendering;

// The AA fringe geometry shared by the per-unit and instanced paths: each contour's feather winding and its triangle
// ring. The ring stores edge directions, not width; the vertex shader builds a 1px miter, so rings are scale-free.
internal static class FringeGeometry
{
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
    /// draw of six corners per edge.</summary>
    public static RingEdge[] BuildRing(List<(Vector2<double>[] Points, float Winding)> contours)
    {
        var total = 0;
        foreach (var (points, _) in contours) total += points.Length;
        var edges = new RingEdge[total];

        var e = 0;
        foreach (var (points, winding) in contours)
        {
            var n = points.Length;
            for (var i = 0; i < n; i++)
            {
                var ni = (i + 1) % n;
                // Winding folded into the directions' sign: reversing a direction reverses its 90-degree normal, which is
                // how a hole feathers inward.
                edges[e++] = new RingEdge
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
    private static void ApplyNesting(List<(Vector2<double>[] Points, float Winding)> built)
    {
        for (var i = 0; i < built.Count; i++)
        {
            var nesting = 0;
            for (var j = 0; j < built.Count; j++)
                if (j != i && PointInPolygon(built[i].Points[0], built[j].Points))
                    nesting++;
            if ((nesting & 1) == 1) built[i] = (built[i].Points, -built[i].Winding);
        }
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
