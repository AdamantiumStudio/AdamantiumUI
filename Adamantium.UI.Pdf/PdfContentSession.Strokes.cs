using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.Pdf.Document;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Curves;

namespace Adamantium.UI.Pdf;

// A pen PDF cannot stroke in one go: one cap for both ends of every dash is all PDF has, so a pen with a trim, dashes, or
// ends unlike each other or PDF's is stroked as the stroke shader lays it - runs along each contour's length, the trim
// window and the dash pattern, stroked without caps; the caps drawn as shapes, a concave one bitten out of its run.
public sealed partial class PdfContentSession
{
    /// <summary>How many periods of dashes a contour is cut into at most; past it, the dashes are finer than a sheet
    /// shows, and the line is drawn whole.</summary>
    private const int MaxDashPeriods = 100_000;

    /// <summary>A stretch of a contour's length that is painted, its ends wearing <see cref="StartCap"/> and
    /// <see cref="EndCap"/>; <see cref="End"/> past the contour's length goes on round a closed one.</summary>
    private readonly record struct StrokeRun(int Contour, double Start, double End, PenLineCap StartCap, PenLineCap EndCap);

    /// <summary>A contour as the stroke measures it: its pieces, the closing one included, and where each starts.</summary>
    private sealed class StrokeContour(List<QuadraticCurve> pieces, double[] starts, double length, bool closed)
    {
        public List<QuadraticCurve> Pieces { get; } = pieces;

        public double[] Starts { get; } = starts;

        public double Length { get; } = length;

        public bool Closed { get; } = closed;
    }

    /// <summary>Whether PDF strokes the pen as it is: no trim, no dashes, one cap for both ends and of PDF's kinds.
    /// </summary>
    private static bool StrokesAsIs(Pen pen) =>
        pen.TrimStart <= 0 && pen.TrimEnd >= 1 && (pen.DashStrokeArray?.Count ?? 0) == 0 &&
        pen.StartLineCap == pen.EndLineCap && pen.StartLineCap is PenLineCap.Flat or PenLineCap.Square or
            PenLineCap.ConvexRound;

    /// <summary>Lengths of a pen measured in the window's units, in the units of the drawing it strokes: the line stays
    /// as wide on the sheet whatever the drawing is scaled by.</summary>
    private double PenUnit(Pen pen, Matrix4x4F transform)
    {
        if (pen.ThicknessUnit != PenThicknessUnit.Window)
        {
            return 1;
        }

        var toRoot = Matrix(transform).Then(_inners.Count > 0 ? _inners.Peek() : PdfMatrix.Identity);
        var scale = Math.Sqrt(Math.Abs(toRoot.A * toRoot.D - toRoot.B * toRoot.C));
        return scale > 1e-12 ? 1 / scale : 1;
    }

    /// <summary>Strokes the pen's runs along <paramref name="curves"/>, in the content's current units: the bites of
    /// concave ends cut out first, the runs stroked without caps, then the convex ends filled with the same paint.
    /// <paramref name="paintCaps"/> fills the path given with the stroke's paint.</summary>
    private bool StrokeRuns(Pen pen, PathCurves curves, double width, Action paintCaps)
    {
        var contours = Contours(curves);
        var runs = Runs(pen, contours, width);
        if (runs.Count == 0)
        {
            return false;
        }

        var content = Content;
        var half = width / 2;
        var bites = false;
        foreach (var run in runs)
        {
            bites |= IsConcave(run.StartCap) || IsConcave(run.EndCap);
        }

        if (bites)
        {
            var box = curves.Bounds;
            var reach = width * 4;
            content.Rectangle(box.X - reach, box.Y - reach, box.Width + reach * 2, box.Height + reach * 2);
            foreach (var run in runs)
            {
                if (NativeCap(run) < 0)
                {
                    Ends(run, contours, half, true);
                }
            }

            content.Clip(true);
        }

        // A run whose two ends are alike and of PDF's kinds is stroked with PDF's own cap, in one piece with its line;
        // the rest without caps, their ends drawn as shapes.
        var drawn = new bool[runs.Count];
        foreach (var cap in (ReadOnlySpan<int>)[0, 1, 2, -1])
        {
            var any = false;
            for (var index = 0; index < runs.Count; index++)
            {
                if (drawn[index] || NativeCap(runs[index]) != cap)
                {
                    continue;
                }

                if (!any)
                {
                    content.LineCap(Math.Max(cap, 0));
                    any = true;
                }

                WriteRun(contours[runs[index].Contour], runs[index].Start, runs[index].End, cap == 1);
                drawn[index] = true;
            }

            if (any)
            {
                content.Stroke();
            }
        }

        var convex = false;
        foreach (var run in runs)
        {
            if (NativeCap(run) < 0)
            {
                convex |= Ends(run, contours, half, false);
            }
        }

        if (convex)
        {
            paintCaps();
        }

        return true;
    }

    /// <summary>The PDF cap a run is stroked with when both its ends are alike and PDF has their kind - 0 butt, 1 round,
    /// 2 projecting square; -1 when its ends are drawn as shapes. A square dot is a shape: PDF does not say which way a
    /// square cap turns on a point.</summary>
    private static int NativeCap(StrokeRun run) => run.StartCap != run.EndCap
        ? -1
        : run.StartCap switch
        {
            PenLineCap.Flat => 0,
            PenLineCap.ConvexRound => 1,
            PenLineCap.Square when run.End > run.Start => 2,
            _ => -1,
        };

    private static bool IsConcave(PenLineCap cap) => cap is PenLineCap.ConcaveTriangle or PenLineCap.ConcaveRound;

    /// <summary>The figures of <paramref name="curves"/> as the stroke measures them.</summary>
    private static List<StrokeContour> Contours(PathCurves curves)
    {
        var contours = new List<StrokeContour>(curves.Figures.Count);
        foreach (var figure in curves.Figures)
        {
            if (figure.Count == 0)
            {
                continue;
            }

            var pieces = new List<QuadraticCurve>(figure.Count + 1);
            for (var index = figure.First; index < figure.First + figure.Count; index++)
            {
                pieces.Add(curves.Curves[index]);
            }

            var first = pieces[0].Start;
            var last = pieces[^1].End;
            if (figure.IsClosed && Length(last - first) > 1e-12)
            {
                pieces.Add(QuadraticCurve.Line(last, first));
            }

            var starts = new double[pieces.Count];
            var length = 0.0;
            for (var index = 0; index < pieces.Count; index++)
            {
                starts[index] = length;
                length += pieces[index].LengthTo(1);
            }

            if (length > 0)
            {
                contours.Add(new StrokeContour(pieces, starts, length, figure.IsClosed));
            }
        }

        return contours;
    }

    /// <summary>The runs the stroke shader paints along each contour (its <c>RunsSd</c>): the trim window - an open
    /// contour's whole length untrimmed, its ends the start and end caps - and, with dashes, each dash in it, its ends
    /// the dash caps but where the window cuts it. A closed contour untrimmed and undashed is one run with no ends.
    /// </summary>
    private static List<StrokeRun> Runs(Pen pen, List<StrokeContour> contours, double width)
    {
        var runs = new List<StrokeRun>();
        var trimStart = Math.Clamp(pen.TrimStart, 0, 1);
        var trimEnd = Math.Clamp(pen.TrimEnd, 0, 1);
        if (trimEnd <= trimStart)
        {
            return runs;
        }

        var unit = pen.Thickness > 0 ? width / pen.Thickness : 1;
        var dashes = pen.DashStrokeArray;
        var count = dashes?.Count ?? 0;
        for (var index = 0; index < contours.Count; index++)
        {
            var contour = contours[index];
            var windowed = !contour.Closed || trimStart > 0 || trimEnd < 1;
            var from = windowed ? trimStart * contour.Length : 0;
            var to = windowed ? trimEnd * contour.Length : contour.Length;
            var period = 0.0;
            for (var entry = 0; entry < count; entry++)
            {
                period += Math.Max(0, dashes[entry]);
            }

            if (count == 0 || period <= 0)
            {
                runs.Add(windowed
                    ? new StrokeRun(index, from, to, pen.StartLineCap, pen.EndLineCap)
                    : new StrokeRun(index, 0, contour.Length, PenLineCap.Flat, PenLineCap.Flat));
                continue;
            }

            // Fitted, a closed contour takes a whole number of periods - stretched by at most half a period.
            var fit = 1.0;
            if (contour.Closed && pen.FitDashesToContour)
            {
                var periods = Math.Max(1, Math.Round(contour.Length / unit / period));
                fit = contour.Length / unit / (periods * period);
            }

            var scale = unit * fit;
            var entries = count % 2 == 1 ? count * 2 : count;
            var total = 0.0;
            for (var entry = 0; entry < entries; entry++)
            {
                total += Math.Max(0, dashes[entry % count]) * scale;
            }

            var offset = (pen.DashOffset * fit + pen.DashPhase * period * fit) * unit;
            var start = -(offset - Math.Floor(offset / total) * total);
            // A hair short of the end: a pattern fitted to the contour ends on it, not a sliver past it.
            var limit = (windowed ? to : contour.Length) - total * 1e-9;
            if ((limit - start) / total > MaxDashPeriods)
            {
                // Dashes finer than any sheet shows: the run whole, as the window has it.
                runs.Add(windowed
                    ? new StrokeRun(index, from, to, pen.StartLineCap, pen.EndLineCap)
                    : new StrokeRun(index, 0, contour.Length, PenLineCap.Flat, PenLineCap.Flat));
                continue;
            }
            while (start < limit)
            {
                for (var entry = 0; entry < entries && start < limit; entry++)
                {
                    var end = start + Math.Max(0, dashes[entry % count]) * scale;
                    if (entry % 2 == 0)
                    {
                        if (windowed)
                        {
                            if (end > from && start < to || end == start && start >= from && start <= to)
                            {
                                runs.Add(new StrokeRun(index, Math.Max(start, from), Math.Min(end, to),
                                    start <= from ? pen.StartLineCap : pen.DashStartCap,
                                    end >= to ? pen.EndLineCap : pen.DashEndCap));
                            }
                        }
                        else if (end > 0 || start >= 0)
                        {
                            // Round a closed contour, a dash across its seam goes on past it.
                            var at = start < 0 ? start + contour.Length : start;
                            runs.Add(new StrokeRun(index, at, at + (end - start), pen.DashStartCap, pen.DashEndCap));
                        }
                    }

                    start = end;
                }
            }
        }

        return runs;
    }

    /// <summary>Writes the run from <paramref name="from"/> to <paramref name="to"/> along the contour as one subpath,
    /// on round a closed one past its seam; a whole closed contour closed. A run of no length is written as a point
    /// when <paramref name="dot"/> - a round cap makes it a dot.</summary>
    private void WriteRun(StrokeContour contour, double from, double to, bool dot = false)
    {
        if (to <= from)
        {
            if (dot && to == from)
            {
                var point = Along(contour, from);
                Content.MoveTo(point.X, point.Y).LineTo(point.X, point.Y);
            }

            return;
        }

        var whole = contour.Closed && from <= 0 && to >= contour.Length;
        var start = Along(contour, from);
        Content.MoveTo(start.X, start.Y);
        var length = contour.Length;
        var at = from;
        while (at < to - 1e-12)
        {
            var lap = Math.Floor(at / length) * length;
            var stop = Math.Min(to, lap + length);
            Span(contour, at - lap, stop - lap);
            at = stop;
        }

        if (whole)
        {
            Content.ClosePath();
        }
    }

    /// <summary>The curves of the contour from <paramref name="from"/> to <paramref name="to"/> along it, within one lap,
    /// the pen already at <paramref name="from"/>.</summary>
    private void Span(StrokeContour contour, double from, double to)
    {
        for (var index = 0; index < contour.Pieces.Count; index++)
        {
            var piece = contour.Pieces[index];
            var start = contour.Starts[index];
            var length = piece.LengthTo(1);
            var end = start + length;
            if (end <= from || start >= to || length <= 0)
            {
                continue;
            }

            var first = from > start ? ParameterAt(piece, from - start, length) : 0;
            var last = to < end ? ParameterAt(piece, to - start, length) : 1;
            var part = Part(piece, first, last);
            if (part.IsStraight())
            {
                Content.LineTo(part.End.X, part.End.Y);
                continue;
            }

            var one = part.Start + (part.Control - part.Start) * (2.0 / 3);
            var two = part.End + (part.Control - part.End) * (2.0 / 3);
            Content.CurveTo(one.X, one.Y, two.X, two.Y, part.End.X, part.End.Y);
        }
    }

    /// <summary>The piece of <paramref name="curve"/> from parameter <paramref name="first"/> to
    /// <paramref name="last"/>.</summary>
    private static QuadraticCurve Part(QuadraticCurve curve, double first, double last)
    {
        var head = last < 1 ? curve.Split(last).First : curve;
        return first > 0 && last > 0 ? head.Split(first / last).Second : head;
    }

    /// <summary>The parameter at which <paramref name="curve"/> is <paramref name="arc"/> long, by halving - as the
    /// shader's measure has it.</summary>
    private static double ParameterAt(QuadraticCurve curve, double arc, double length)
    {
        if (arc <= 0)
        {
            return 0;
        }

        if (arc >= length)
        {
            return 1;
        }

        var low = 0.0;
        var high = 1.0;
        for (var step = 0; step < 40; step++)
        {
            var middle = (low + high) / 2;
            if (curve.LengthTo(middle) < arc)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        return (low + high) / 2;
    }

    /// <summary>Where the contour is <paramref name="arc"/> along it - round a closed one past its seam.</summary>
    private static Vector2<double> Along(StrokeContour contour, double arc) => Frame(contour, arc, out _);

    /// <summary>Where the contour is <paramref name="arc"/> along it, and which way it runs there.</summary>
    private static Vector2<double> Frame(StrokeContour contour, double arc, out Vector2<double> direction)
    {
        if (contour.Closed)
        {
            arc -= Math.Floor(arc / contour.Length) * contour.Length;
        }

        arc = Math.Clamp(arc, 0, contour.Length);
        var index = contour.Pieces.Count - 1;
        while (index > 0 && contour.Starts[index] > arc)
        {
            index--;
        }

        var piece = contour.Pieces[index];
        var length = piece.LengthTo(1);
        var parameter = ParameterAt(piece, arc - contour.Starts[index], length);
        var speed = (piece.Control - piece.Start) * (2 * (1 - parameter)) +
                    (piece.End - piece.Control) * (2 * parameter);
        if (Length(speed) <= 1e-12)
        {
            speed = piece.End - piece.Start;
        }

        var size = Length(speed);
        direction = size > 0 ? speed * (1 / size) : new Vector2<double>(1, 0);
        return piece.At(parameter);
    }

    /// <summary>Writes the shapes of the run's ends - the bites of its concave ones (<paramref name="bites"/>) or the
    /// convex ones painted past it - and says whether it wrote any.</summary>
    private bool Ends(StrokeRun run, List<StrokeContour> contours, double half, bool bites)
    {
        var contour = contours[run.Contour];
        var bite = Math.Min(half, 0.5 * (run.End - run.Start));
        var wrote = false;
        var at = Frame(contour, run.Start, out var along);
        wrote |= End(run.StartCap, at, along * -1, half, bite, bites);
        at = Frame(contour, run.End, out along);
        wrote |= End(run.EndCap, at, along, half, bite, bites);
        return wrote;
    }

    /// <summary>The shape of one end at <paramref name="at"/>, <paramref name="outward"/> pointing away from its run
    /// (the shader's <c>CapSd</c>): a square nub, a half disc or a tip painted past the end, or a notch or an arc of
    /// <paramref name="bite"/> bitten into it.</summary>
    private bool End(PenLineCap cap, Vector2<double> at, Vector2<double> outward, double half, double bite, bool bites)
    {
        var across = new Vector2<double>(-outward.Y, outward.X);
        if (bites)
        {
            switch (cap)
            {
                case PenLineCap.ConcaveTriangle when bite > 0:
                    Polygon([at + across * bite, at - outward * bite, at - across * bite], false);
                    return true;
                case PenLineCap.ConcaveRound when bite > 0:
                    Circle(at, bite);
                    return true;
                default:
                    return false;
            }
        }

        switch (cap)
        {
            case PenLineCap.Square:
                Polygon([at + across * half, at + across * half + outward * half, at - across * half + outward * half,
                    at - across * half], false);
                return true;
            case PenLineCap.ConvexTriangle:
                Polygon([at + across * half, at + outward * half, at - across * half], false);
                return true;
            case PenLineCap.ConvexRound:
                HalfDisc(at, outward, across, half);
                return true;
            default:
                return false;
        }
    }

    /// <summary>The half disc of <paramref name="radius"/> on the <paramref name="outward"/> side of
    /// <paramref name="center"/>, by two quarter arcs.</summary>
    private void HalfDisc(Vector2<double> center, Vector2<double> outward, Vector2<double> across, double radius)
    {
        var content = Content;
        var hold = radius * Kappa;
        var one = center + across * radius;
        var tip = center + outward * radius;
        var two = center - across * radius;
        content.MoveTo(one.X, one.Y);
        var leaving = one + outward * hold;
        var reaching = tip + across * hold;
        content.CurveTo(leaving.X, leaving.Y, reaching.X, reaching.Y, tip.X, tip.Y);
        leaving = tip - across * hold;
        reaching = two + outward * hold;
        content.CurveTo(leaving.X, leaving.Y, reaching.X, reaching.Y, two.X, two.Y);
        content.ClosePath();
    }
}
