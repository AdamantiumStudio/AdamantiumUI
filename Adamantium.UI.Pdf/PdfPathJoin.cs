using System;
using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Pdf.Steps;

namespace Adamantium.UI.Pdf;

/// <summary>Joins the paths a page draws one after another with the same paint into one path a part of the page, as a
/// map's tile draws its features of one style in one draw: a renderer that walks every draw each frame walks a few
/// thousand where it walked tens. The page looks the same: only opaque solid paint is joined - a fill under the nonzero
/// rule, a stroke without dashes - and only paths placed by the same matrix, so what one covers, the joined covers. The
/// interpreter places a path that has a translation of its own by the corner of a grid's square instead, so that paths of
/// one part of the page share their matrix.</summary>
internal static class PdfPathJoin
{
    /// <summary>The draws of <paramref name="steps"/> with the runs of paths joined; <paramref name="pressable"/>'s
    /// places among them are moved with them into <paramref name="moved"/>, and no run is joined across one.</summary>
    public static List<PdfDrawStep> Join(IReadOnlyList<PdfDrawStep> steps, IReadOnlyList<PdfPressableSteps> pressable,
        out List<PdfPressableSteps> moved)
    {
        var edges = new HashSet<int>();
        foreach (var range in pressable)
        {
            edges.Add(range.Start);
            edges.Add(range.End);
        }

        var at = new Dictionary<int, int>();
        var joined = new List<PdfDrawStep>(steps.Count);
        var cells = new List<Cell>();
        var byCell = new Dictionary<Cell, List<PdfGeometryStep>>();
        var index = 0;
        while (index < steps.Count)
        {
            if (edges.Contains(index))
            {
                at[index] = joined.Count;
            }

            var end = index + 1;
            if (steps[index] is PdfGeometryStep first && Joins(first, out var turn))
            {
                while (end < steps.Count && !edges.Contains(end) && steps[end] is PdfGeometryStep next &&
                       Joins(next, out var nextTurn) && nextTurn == turn && Alike(first, next))
                {
                    end++;
                }
            }

            if (end - index == 1)
            {
                joined.Add(steps[index]);
            }
            else
            {
                JoinRun(steps, index, end, joined, cells, byCell);
            }

            index = end;
        }

        if (edges.Contains(steps.Count))
        {
            at[steps.Count] = joined.Count;
        }

        moved = new List<PdfPressableSteps>(pressable.Count);
        foreach (var range in pressable)
        {
            moved.Add(new PdfPressableSteps(range.Annotation, at[range.Start], at[range.End], range.Down));
        }

        return joined;
    }

    // A part of the page the side of this many points: what is joined stays in one, so a view still leaves out what is
    // far off - on a page that places every path by one matrix, as most do, too.
    private const double CellSide = 256;

    // Where a path is joined: placed by the same matrix - its translation the same - and begun in the same part of the
    // page.
    private readonly record struct Cell(float X, float Y, int Column, int Row);

    // One path for the run's paths of one cell; the paint the same and opaque, they may come in any order.
    private static void JoinRun(IReadOnlyList<PdfDrawStep> steps, int from, int to, List<PdfDrawStep> into,
        List<Cell> cells, Dictionary<Cell, List<PdfGeometryStep>> byCell)
    {
        cells.Clear();
        for (var index = from; index < to; index++)
        {
            var path = (PdfGeometryStep)steps[index];
            var cell = CellOf(path);
            if (!byCell.TryGetValue(cell, out var paths))
            {
                paths = [];
                byCell[cell] = paths;
            }

            if (paths.Count == 0)
            {
                cells.Add(cell);
            }

            paths.Add(path);
        }

        foreach (var cell in cells)
        {
            var paths = byCell[cell];
            into.Add(paths.Count == 1 ? paths[0] : Joined(paths));

            // Emptied for the next run: only the cells this run used.
            paths.Clear();
        }
    }

    private static Cell CellOf(PdfGeometryStep path)
    {
        var matrix = path.Matrix;
        var figures = ((StreamGeometry)path.Geometry).Figures;
        var start = figures.Count > 0 ? figures[0].StartPoint : default;
        var x = start.X * matrix.M11 + start.Y * matrix.M21 + matrix.M41;
        var y = start.X * matrix.M12 + start.Y * matrix.M22 + matrix.M42;
        return new Cell(matrix.M41, matrix.M42, (int)Math.Floor(x / CellSide), (int)Math.Floor(y / CellSide));
    }

    private static PdfGeometryStep Joined(List<PdfGeometryStep> paths)
    {
        var first = paths[0];
        var geometry = new StreamGeometry { FillRule = ((StreamGeometry)first.Geometry).FillRule };
        var context = geometry.Open();
        foreach (var path in paths)
        {
            foreach (var figure in ((StreamGeometry)path.Geometry).Figures)
            {
                // The figure's own segments, not copies of their points; taken out of the figure they came from, which
                // would otherwise stay held through them.
                context.BeginFigure(figure.StartPoint, figure.IsFilled, figure.IsClosed);
                context.Figures[^1].Segments.AddRange(figure.Segments);
                figure.Segments.Clear();
            }
        }

        return new PdfGeometryStep(geometry, first.Fill, first.Stroke, first.Matrix) { IsPagePath = first.IsPagePath };
    }

    // Filled or stroked, not both, with opaque solid paint; a fill by the nonzero rule, a stroke without dashes. A fill's
    // figures all turn one way, given out in turn: fills joined turning alike add where they overlap, where two turning
    // apart would cancel each other out and leave a hole. A stroke's turn is 0.
    private static bool Joins(PdfGeometryStep path, out int turn)
    {
        turn = 0;
        if (path.Geometry is not StreamGeometry geometry)
        {
            return false;
        }

        if (path.Fill != null)
        {
            if (path.Stroke != null || !Opaque(path.Fill) || geometry.FillRule != FillRule.NonZero)
            {
                return false;
            }

            turn = TurnOf(geometry);
            return turn != TurnsBothWays;
        }

        return path.Stroke is { } pen && pen.Dashes.Count == 0 && Opaque(pen.Brush);
    }

    private const int TurnsBothWays = 2;

    // 1 or -1 for figures that all turn that way, 0 for none with an area, TurnsBothWays for some each way - or any of a
    // shape not known here.
    private static int TurnOf(StreamGeometry geometry)
    {
        var turn = 0;
        foreach (var figure in geometry.Figures)
        {
            var area = TwiceTheAreaOf(figure);
            if (double.IsNaN(area))
            {
                return TurnsBothWays;
            }

            var sign = Math.Sign(area);
            if (sign == 0)
            {
                continue;
            }

            if (turn == 0)
            {
                turn = sign;
            }
            else if (sign != turn)
            {
                return TurnsBothWays;
            }
        }

        return turn;
    }

    // The signed area of the polygon through a figure's points - a curve's by its control points, which turn as it does.
    private static double TwiceTheAreaOf(PathFigure figure)
    {
        var area = 0.0;
        var from = figure.StartPoint;
        foreach (var segment in figure.Segments)
        {
            switch (segment)
            {
                case PolylineSegment polyline:
                    foreach (var point in polyline.Points.AsSpan())
                    {
                        area += from.X * point.Y - point.X * from.Y;
                        from = point;
                    }

                    break;
                case LineSegment line:
                    area += from.X * line.Point.Y - line.Point.X * from.Y;
                    from = line.Point;
                    break;
                case CubicBezierSegment curve:
                    area += from.X * curve.ControlPoint1.Y - curve.ControlPoint1.X * from.Y;
                    area += curve.ControlPoint1.X * curve.ControlPoint2.Y - curve.ControlPoint2.X * curve.ControlPoint1.Y;
                    area += curve.ControlPoint2.X * curve.Point.Y - curve.Point.X * curve.ControlPoint2.Y;
                    from = curve.Point;
                    break;
                default:
                    return double.NaN;
            }
        }

        var start = figure.StartPoint;
        return area + from.X * start.Y - start.X * from.Y;
    }

    // The same paint - the page's brushes and pens are made once a paint - scaled and turned alike: a stroke as wide.
    private static bool Alike(PdfGeometryStep first, PdfGeometryStep next)
    {
        var one = first.Matrix;
        var two = next.Matrix;
        return ReferenceEquals(first.Fill, next.Fill) && ReferenceEquals(first.Stroke, next.Stroke) &&
               first.IsPagePath == next.IsPagePath && one.M11 == two.M11 && one.M12 == two.M12 &&
               one.M21 == two.M21 && one.M22 == two.M22 && one.M44 == two.M44 && one.M14 == two.M14 &&
               one.M24 == two.M24;
    }

    private static bool Opaque(Brush brush) => brush is SolidColorBrush { Color.A: 255, Opacity: >= 1 };
}
