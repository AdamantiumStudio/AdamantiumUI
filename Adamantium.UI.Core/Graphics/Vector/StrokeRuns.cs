using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Curves;

namespace Adamantium.UI.Core.Graphics.Vector;

/// <summary>A stretch of a contour's length that is painted, its ends wearing <see cref="StartCap"/> and
/// <see cref="EndCap"/>; <see cref="End"/> past the contour's length goes on round a closed one.</summary>
internal readonly record struct StrokeRun(int Contour, double Start, double End, PenLineCap StartCap, PenLineCap EndCap)
{
    /// <summary>Whether either end is bitten into its run.</summary>
    public bool IsConcave => IsBite(StartCap) || IsBite(EndCap);

    public static bool IsBite(PenLineCap cap) => cap is PenLineCap.ConcaveTriangle or PenLineCap.ConcaveRound;
}

/// <summary>A pen laid along a path as the stroke shader lays it (its <c>RunsSd</c> and <c>CapSd</c>): runs along each
/// contour's length - the trim window and the dash pattern, fitted or not - each with the kind of end it wears, and the
/// shapes of those ends. A vector writer whose own stroking cannot say all that strokes the runs without ends and fills
/// the ends as shapes.</summary>
internal sealed class StrokeRuns
{
    /// <summary>How many periods of dashes a contour is cut into at most; past it, the dashes are finer than a sheet
    /// shows, and the line is drawn whole.</summary>
    public const int MaxDashPeriods = 100_000;

    private readonly List<Contour> _contours;

    private StrokeRuns(List<Contour> contours, List<StrokeRun> runs)
    {
        _contours = contours;
        Runs = runs;
    }

    public IReadOnlyList<StrokeRun> Runs { get; }

    /// <summary>A contour as the stroke measures it: its pieces, the closing one included, and where each starts.
    /// </summary>
    private sealed class Contour(List<QuadraticCurve> pieces, double[] starts, double length, bool closed)
    {
        public List<QuadraticCurve> Pieces { get; } = pieces;

        public double[] Starts { get; } = starts;

        public double Length { get; } = length;

        public bool Closed { get; } = closed;
    }

    /// <summary>The runs <paramref name="pen"/> paints along <paramref name="curves"/>, <paramref name="width"/> wide in
    /// the curves' units.</summary>
    public static StrokeRuns Lay(Pen pen, PathCurves curves, double width)
    {
        ArgumentNullException.ThrowIfNull(pen);
        ArgumentNullException.ThrowIfNull(curves);
        var contours = Contours(curves);
        return new StrokeRuns(contours, RunsOf(pen, contours, width));
    }

    private static List<Contour> Contours(PathCurves curves)
    {
        var contours = new List<Contour>(curves.Figures.Count);
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
            if (figure.IsClosed && VectorShapes.Length(last - first) > 1e-12)
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
                contours.Add(new Contour(pieces, starts, length, figure.IsClosed));
            }
        }

        return contours;
    }

    /// <summary>The trim window - an open contour's whole length untrimmed, its ends the start and end caps - and, with
    /// dashes, each dash in it, its ends the dash caps but where the window cuts it. A closed contour untrimmed and
    /// undashed is one run with no ends.</summary>
    private static List<StrokeRun> RunsOf(Pen pen, List<Contour> contours, double width)
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

            var first = runs.Count;
            while (start < limit)
            {
                for (var entry = 0; entry < entries && start < limit; entry++)
                {
                    var end = start + Math.Max(0, dashes[entry % count]) * scale;
                    if (entry % 2 == 0)
                    {
                        if (windowed)
                        {
                            // A dash that reaches an end of the window to within the length's own rounding wears
                            // the line's end there.
                            var near = total * 1e-9;
                            if (end > from && start < to || end == start && start >= from && start <= to)
                            {
                                runs.Add(new StrokeRun(index, Math.Max(start, from), Math.Min(end, to),
                                    start <= from + near ? pen.StartLineCap : pen.DashStartCap,
                                    end >= to - near ? pen.EndLineCap : pen.DashEndCap));
                            }
                        }
                        else if (end > 0 || start >= 0)
                        {
                            // The pattern runs straight along the contour, its seam a plain cut, as the shader lays it.
                            runs.Add(new StrokeRun(index, Math.Max(start, 0), Math.Min(end, contour.Length),
                                start < 0 ? PenLineCap.Flat : pen.DashStartCap,
                                end > contour.Length ? PenLineCap.Flat : pen.DashEndCap));
                        }
                    }

                    start = end;
                }
            }

            if (!windowed)
            {
                Seam(runs, first, contour.Length);
            }
        }

        return runs;
    }

    /// <summary>Joins a closed contour's dash cut by its start and the one cut by its end into one going on through
    /// the seam: both are painted there, so they meet as one line.</summary>
    private static void Seam(List<StrokeRun> runs, int first, double length)
    {
        var last = runs.Count - 1;
        if (last <= first)
        {
            return;
        }

        var head = runs[first];
        var tail = runs[last];
        if (head.Start > 0 || tail.End < length || head.StartCap != PenLineCap.Flat ||
            tail.EndCap != PenLineCap.Flat)
        {
            return;
        }

        runs[last] = tail with { End = length + head.End, EndCap = head.EndCap };
        runs.RemoveAt(first);
    }

    /// <summary>Writes <paramref name="run"/> along its contour as one figure, on round a closed one past its seam; a
    /// whole closed contour closed. A run of no length is written as a point when <paramref name="dot"/> - a round cap
    /// makes it a dot.</summary>
    public void WriteRun(IVectorPath path, StrokeRun run, bool dot = false)
    {
        var contour = _contours[run.Contour];
        var from = run.Start;
        var to = run.End;
        if (to <= from)
        {
            if (dot && to == from)
            {
                var point = Frame(contour, from, out _);
                path.MoveTo(point);
                path.LineTo(point);
            }

            return;
        }

        var length = contour.Length;
        // Round a closed contour at most once: more paints nothing more.
        to = Math.Min(to, from + length);
        var whole = contour.Closed && from <= 0 && to >= length;
        path.MoveTo(Frame(contour, from, out _));
        var lap = (int)Math.Floor(from / length);
        var at = from;
        while (at < to - 1e-12 && lap <= (int)Math.Floor(to / length))
        {
            var lapStart = lap * length;
            var stop = Math.Min(to, lapStart + length);
            Span(path, contour, Math.Max(0, at - lapStart), stop - lapStart);
            at = stop;
            lap++;
        }

        if (whole)
        {
            path.Close();
        }
    }

    /// <summary>Writes the shapes of the run's ends - the bites of its concave ones (<paramref name="bites"/>) or the
    /// convex ones painted past it - and says whether it wrote any.</summary>
    public bool WriteEnds(IVectorPath path, StrokeRun run, double half, bool bites)
    {
        var contour = _contours[run.Contour];
        var bite = Math.Min(half, 0.5 * (run.End - run.Start));
        var wrote = false;
        var at = Frame(contour, run.Start, out var along);
        wrote |= End(path, run.StartCap, at, along * -1, half, bite, bites);
        at = Frame(contour, run.End, out along);
        wrote |= End(path, run.EndCap, at, along, half, bite, bites);
        return wrote;
    }

    /// <summary>The curves of the contour from <paramref name="from"/> to <paramref name="to"/> along it, within one lap,
    /// the pen already at <paramref name="from"/>.</summary>
    private static void Span(IVectorPath path, Contour contour, double from, double to)
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
                path.LineTo(part.End);
            }
            else
            {
                path.QuadraticTo(part.Control, part.End);
            }
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

    /// <summary>Where the contour is <paramref name="arc"/> along it - round a closed one past its seam - and which way
    /// it runs there.</summary>
    private static Vector2<double> Frame(Contour contour, double arc, out Vector2<double> direction)
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
        if (VectorShapes.Length(speed) <= 1e-12)
        {
            speed = piece.End - piece.Start;
        }

        var size = VectorShapes.Length(speed);
        direction = size > 0 ? speed * (1 / size) : new Vector2<double>(1, 0);
        return piece.At(parameter);
    }

    /// <summary>The shape of one end at <paramref name="at"/>, <paramref name="outward"/> pointing away from its run:
    /// a square nub, a half disc or a tip painted past the end, or a notch or an arc of <paramref name="bite"/> bitten
    /// into it.</summary>
    private static bool End(IVectorPath path, PenLineCap cap, Vector2<double> at, Vector2<double> outward, double half,
        double bite, bool bites)
    {
        var across = new Vector2<double>(-outward.Y, outward.X);
        if (bites)
        {
            switch (cap)
            {
                case PenLineCap.ConcaveTriangle when bite > 0:
                    VectorShapes.AddPolygon(path, [at + across * bite, at - outward * bite, at - across * bite], false);
                    return true;
                case PenLineCap.ConcaveRound when bite > 0:
                    VectorShapes.AddCircle(path, at, bite);
                    return true;
                default:
                    return false;
            }
        }

        switch (cap)
        {
            case PenLineCap.Square:
                VectorShapes.AddPolygon(path, [at + across * half, at + across * half + outward * half,
                    at - across * half + outward * half, at - across * half], false);
                return true;
            case PenLineCap.ConvexTriangle:
                VectorShapes.AddPolygon(path, [at + across * half, at + outward * half, at - across * half], false);
                return true;
            case PenLineCap.ConvexRound:
                VectorShapes.AddHalfDisc(path, at, outward, across, half);
                return true;
            default:
                return false;
        }
    }
}
