using System;
using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.UI.Core;

namespace Adamantium.UI.Controls.DrawingBoard;

internal static class CanvasTouch
{
    // Touching counts: a stroke exactly on the edge of the viewport is visible, and an item with no thickness in one
    // direction (a horizontal line) has a zero-height box that must still meet the world it lies in.
    public static bool Meets(Rect item, Rect world) =>
        item.X <= world.X + world.Width && item.X + item.Width >= world.X &&
        item.Y <= world.Y + world.Height && item.Y + item.Height >= world.Y;

    public static Vector2[] Corners(Rect r) =>
    [
        new(r.X, r.Y), new(r.X + r.Width, r.Y), new(r.X + r.Width, r.Y + r.Height), new(r.X, r.Y + r.Height)
    ];

    public static bool AllInside(IReadOnlyList<Vector2> points, Rect r)
    {
        foreach (var p in points)
        {
            if (p.X < r.X || p.X > r.X + r.Width || p.Y < r.Y || p.Y > r.Y + r.Height) return false;
        }

        return true;
    }

    public static bool RunTouches(IReadOnlyList<Vector2> points, double reach, Rect world)
    {
        if (points.Count == 0) return false;

        var grown = world.Inflate(Math.Max(0, reach));
        if (points.Count == 1) return grown.Contains(points[0]);

        for (var i = 1; i < points.Count; i++)
        {
            if (SegmentMeets(points[i - 1], points[i], grown)) return true;
        }

        return false;
    }

    public static bool SegmentMeets(Vector2 a, Vector2 b, Rect r)
    {
        var t0 = 0.0;
        var t1 = 1.0;
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;

        return Clip(-dx, a.X - r.X, ref t0, ref t1)
               && Clip(dx, r.X + r.Width - a.X, ref t0, ref t1)
               && Clip(-dy, a.Y - r.Y, ref t0, ref t1)
               && Clip(dy, r.Y + r.Height - a.Y, ref t0, ref t1);
    }

    public static bool ConvexOverlap(IReadOnlyList<Vector2> a, IReadOnlyList<Vector2> b) =>
        !Separates(a, a, b) && !Separates(b, a, b);

    public static bool CircleOverlap(IReadOnlyList<Vector2> polygon, Vector2 center, double radius)
    {
        if (ConvexContains(polygon, center)) return true;

        for (var i = 0; i < polygon.Count; i++)
        {
            if (Distance(center, polygon[i], polygon[(i + 1) % polygon.Count]) <= radius) return true;
        }

        return false;
    }

    public static bool ConvexContains(IReadOnlyList<Vector2> polygon, Vector2 p)
    {
        var sign = 0;
        for (var i = 0; i < polygon.Count; i++)
        {
            var from = polygon[i];
            var to = polygon[(i + 1) % polygon.Count];
            var cross = (to.X - from.X) * (p.Y - from.Y) - (to.Y - from.Y) * (p.X - from.X);
            if (Math.Abs(cross) < 1e-12) continue;

            var side = cross > 0 ? 1 : -1;
            if (sign == 0) sign = side;
            else if (side != sign) return false;
        }

        return sign != 0;
    }

    public static Vector2[] Thick(Vector2 from, Vector2 to, double reach)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        var r = Math.Max(reach, 1e-9);

        if (length < 1e-12)
        {
            return [new(from.X - r, from.Y - r), new(from.X + r, from.Y - r), new(from.X + r, from.Y + r), new(from.X - r, from.Y + r)];
        }

        var ux = dx / length * r;
        var uy = dy / length * r;

        return
        [
            new(from.X - ux - uy, from.Y - uy + ux), new(to.X + ux - uy, to.Y + uy + ux),
            new(to.X + ux + uy, to.Y + uy - ux), new(from.X - ux + uy, from.Y - uy - ux)
        ];
    }

    public static double Distance(Vector2 point, Vector2 from, Vector2 to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var lengthSquared = dx * dx + dy * dy;

        var t = lengthSquared <= double.Epsilon
            ? 0
            : Math.Clamp(((point.X - from.X) * dx + (point.Y - from.Y) * dy) / lengthSquared, 0, 1);

        var px = point.X - (from.X + t * dx);
        var py = point.Y - (from.Y + t * dy);

        return Math.Sqrt(px * px + py * py);
    }

    private static bool Clip(double p, double q, ref double t0, ref double t1)
    {
        if (Math.Abs(p) < 1e-12) return q >= 0;

        var t = q / p;
        if (p < 0)
        {
            if (t > t1) return false;
            if (t > t0) t0 = t;
        }
        else
        {
            if (t < t0) return false;
            if (t < t1) t1 = t;
        }

        return true;
    }

    private static bool Separates(IReadOnlyList<Vector2> edges, IReadOnlyList<Vector2> a, IReadOnlyList<Vector2> b)
    {
        for (var i = 0; i < edges.Count; i++)
        {
            var from = edges[i];
            var to = edges[(i + 1) % edges.Count];
            var nx = -(to.Y - from.Y);
            var ny = to.X - from.X;
            if (Math.Abs(nx) < 1e-12 && Math.Abs(ny) < 1e-12) continue;

            Project(a, nx, ny, out var aMin, out var aMax);
            Project(b, nx, ny, out var bMin, out var bMax);

            if (aMax < bMin || bMax < aMin) return true;
        }

        return false;
    }

    private static void Project(IReadOnlyList<Vector2> points, double nx, double ny, out double min, out double max)
    {
        min = double.MaxValue;
        max = double.MinValue;

        foreach (var p in points)
        {
            var d = p.X * nx + p.Y * ny;
            min = Math.Min(min, d);
            max = Math.Max(max, d);
        }
    }
}
