using Adamantium.Mathematics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Curves;

namespace Adamantium.UI.Core.Graphics.Vector;

/// <summary>Shapes vector writers draw the same way: a path's curves, discs, polygons, ink as its passes shape it, and
/// arrows. Each is written into an <see cref="IVectorPath"/> to be filled.</summary>
internal static class VectorShapes
{
    /// <summary>How far a cubic's handles stand off for a quarter circle of radius 1.</summary>
    public const double Kappa = 0.5522847498307936;

    /// <summary>Writes the figures of <paramref name="curves"/> - only the filled ones when
    /// <paramref name="filledOnly"/> - and says whether any was.</summary>
    public static bool AddCurves(IVectorPath path, PathCurves curves, bool filledOnly)
    {
        var wrote = false;
        foreach (var figure in curves.Figures)
        {
            if (figure.Count == 0 || (filledOnly && !figure.IsFilled))
            {
                continue;
            }

            path.MoveTo(curves.Curves[figure.First].Start);
            for (var index = figure.First; index < figure.First + figure.Count; index++)
            {
                var curve = curves.Curves[index];
                if (curve.IsStraight())
                {
                    path.LineTo(curve.End);
                }
                else
                {
                    path.QuadraticTo(curve.Control, curve.End);
                }
            }

            if (figure.IsClosed)
            {
                path.Close();
            }

            wrote = true;
        }

        return wrote;
    }

    public static void AddCircle(IVectorPath path, Vector2<double> center, double radius)
    {
        if (!(radius > 0))
        {
            return;
        }

        var hold = radius * Kappa;
        path.MoveTo(new Vector2<double>(center.X + radius, center.Y));
        path.CubicTo(new Vector2<double>(center.X + radius, center.Y + hold),
            new Vector2<double>(center.X + hold, center.Y + radius), new Vector2<double>(center.X, center.Y + radius));
        path.CubicTo(new Vector2<double>(center.X - hold, center.Y + radius),
            new Vector2<double>(center.X - radius, center.Y + hold), new Vector2<double>(center.X - radius, center.Y));
        path.CubicTo(new Vector2<double>(center.X - radius, center.Y - hold),
            new Vector2<double>(center.X - hold, center.Y - radius), new Vector2<double>(center.X, center.Y - radius));
        path.CubicTo(new Vector2<double>(center.X + hold, center.Y - radius),
            new Vector2<double>(center.X + radius, center.Y - hold), new Vector2<double>(center.X + radius, center.Y));
        path.Close();
    }

    /// <summary>A polygon turned one way - the other for a <paramref name="hole"/>, which the nonzero rule then cuts.
    /// </summary>
    public static void AddPolygon(IVectorPath path, Vector2<double>[] points, bool hole)
    {
        var area = 0.0;
        for (var index = 0; index < points.Length; index++)
        {
            var next = points[(index + 1) % points.Length];
            area += points[index].X * next.Y - next.X * points[index].Y;
        }

        if ((area < 0) != hole)
        {
            Array.Reverse(points);
        }

        path.MoveTo(points[0]);
        for (var index = 1; index < points.Length; index++)
        {
            path.LineTo(points[index]);
        }

        path.Close();
    }

    /// <summary>The half disc of <paramref name="radius"/> on the <paramref name="outward"/> side of
    /// <paramref name="center"/>, by two quarter arcs.</summary>
    public static void AddHalfDisc(IVectorPath path, Vector2<double> center, Vector2<double> outward,
        Vector2<double> across, double radius)
    {
        var hold = radius * Kappa;
        var one = center + across * radius;
        var tip = center + outward * radius;
        var two = center - across * radius;
        path.MoveTo(one);
        path.CubicTo(one + outward * hold, tip + across * hold, tip);
        path.CubicTo(tip - across * hold, two + outward * hold, two);
        path.Close();
    }

    /// <summary>A line of <paramref name="radius"/> with round ends.</summary>
    public static void AddCapsule(IVectorPath path, Vector2<double> from, Vector2<double> to, double radius)
    {
        if (!(radius > 0))
        {
            return;
        }

        AddCircle(path, from, radius);
        if (Length(to - from) <= 1e-9)
        {
            return;
        }

        var side = Across(to - from);
        AddPolygon(path, [from + side * radius, to + side * radius, to - side * radius, from - side * radius], false);
        AddCircle(path, to, radius);
    }

    /// <summary>Ink of varying width as its passes shape it: a disc at each point, joined to the last by a quad as wide
    /// as each end. Returns the widest radius - how far past its points it reaches.</summary>
    public static double AddInk(IVectorPath path, InkBrush ink, int count)
    {
        var points = ink.Points;
        var widths = ink.Widths;
        var widest = 0.0;
        for (var index = 0; index < count; index++)
        {
            var radius = ink.Thickness * Share(widths, index) / 2;
            widest = Math.Max(widest, radius);
            AddCircle(path, At(points[index]), radius);
            if (index == 0)
            {
                continue;
            }

            var from = At(points[index - 1]);
            var to = At(points[index]);
            var side = Across(to - from);
            var before = ink.Thickness * Share(widths, index - 1) / 2;
            AddPolygon(path, [from + side * before, to + side * radius, to - side * radius, from - side * before], false);
        }

        return widest;
    }

    /// <summary>An arrow as its shader shapes it: the shaft a capsule ended short of each head, the heads triangles, an
    /// outlined head (kind 1) notched to the line's width.</summary>
    public static void AddArrow(IVectorPath path, CanvasArrowBrush arrow)
    {
        var from = arrow.From;
        var to = arrow.To;
        var span = Math.Max(Length(to - from), 1e-6);
        var along = (to - from) * (1 / span);
        var edge = arrow.Thickness / 2;
        var wide = arrow.HeadWidth;
        var length = arrow.HeadLength;
        var back = Math.Min(length, span);
        var tuck = edge / Math.Max(wide / Math.Sqrt(Math.Max(length * length + wide * wide, 1e-12)), 1e-6);
        var shaftFrom = arrow.StartHead >= 1 ? from + along * Math.Min(arrow.StartHead >= 2 ? length : tuck, span) : from;
        var shaftTo = arrow.EndHead >= 1 ? to - along * Math.Min(arrow.EndHead >= 2 ? length : tuck, span) : to;
        AddCapsule(path, shaftFrom, shaftTo, edge);
        AddHead(path, arrow.StartHead, from, along * -1, back, wide, edge);
        AddHead(path, arrow.EndHead, to, along, back, wide, edge);
    }

    private static void AddHead(IVectorPath path, int kind, Vector2<double> tip, Vector2<double> forward, double back,
        double wide, double edge)
    {
        if (kind < 1 || !(back > 0) || !(wide > 0))
        {
            return;
        }

        var side = Across(forward);
        var bottom = tip - forward * back;
        AddPolygon(path, [tip, bottom + side * wide, bottom - side * wide], false);
        if (kind >= 2)
        {
            return;
        }

        var slant = 1 / Math.Sqrt(back * back + wide * wide);
        var apex = edge / (wide * slant);
        var inner = wide - edge / (slant * back);
        if (apex < back && inner > 0)
        {
            var notch = tip - forward * apex;
            AddPolygon(path, [notch, bottom + side * inner, bottom - side * inner], true);
        }
    }

    internal static double Length(Vector2<double> vector) => Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y);

    internal static Vector2<double> Across(Vector2<double> along)
    {
        var length = Length(along);
        return length > 0 ? new Vector2<double>(-along.Y / length, along.X / length) : default;
    }

    private static Vector2<double> At(Vector2<float> point) => new(point.X, point.Y);

    private static double Share(float[] widths, int index) =>
        widths == null ? 1 : index < widths.Length ? Math.Max(0, widths[index]) : 1;
}
