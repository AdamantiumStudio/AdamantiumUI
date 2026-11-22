using System;
using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Pdf.Steps;

namespace Adamantium.UI.Pdf;

/// <summary>A long stroked line of a page drawn with fewer points while it is small on screen - a map's lines at its
/// zoom levels: at each level, the line simplified with a coarser tolerance, every point it leaves out within that of
/// the line it keeps. How much each point matters is worked out once, where the page is prepared; a level's copy is
/// made the first time it is drawn - made there too, the copies of every level kept, they cost the collector more than
/// they save. Only a line of straight pieces: one that bends is drawn as given, at every zoom.</summary>
internal sealed class PdfStrokeDetail
{
    /// <summary>How far from the line the finest level may leave a point out, in the page's points; each coarser level
    /// twice as far.</summary>
    public const double FinestTolerance = 1.0 / 256;

    /// <summary>The levels: the coarsest leaves points out within 1 pt.</summary>
    public const int Levels = 9;

    // How far from the line a point may be left out where the page is drawn, in device pixels.
    private const double UnseenPixels = 0.25;

    // A shorter line costs too little to be worth levels.
    private const int FewestPieces = 256;

    // A level keeping more than this share of the points of the finer one before it draws as that one does.
    private const double WorthMaking = 0.9;

    private readonly PdfGeometryStep _step;
    private readonly List<Figure> _figures;
    private readonly double _finest;

    // For each level, the finer level whose copy it draws - 0, the line as given - and the copies made so far.
    private readonly int[] _drawnAs;
    private readonly PdfGeometryStep[] _made = new PdfGeometryStep[Levels];

    private PdfStrokeDetail(PdfGeometryStep step, List<Figure> figures, double finest, int[] drawnAs)
    {
        _step = step;
        _figures = figures;
        _finest = finest;
        _drawnAs = drawnAs;
    }

    /// <summary>Which level a page shown at no more than <paramref name="finestPixelsPerPoint"/> device pixels to its
    /// point draws: the coarsest whose left-out points stay within a quarter of a pixel; 0, the lines as given.</summary>
    public static int LevelFor(double finestPixelsPerPoint)
    {
        if (!(finestPixelsPerPoint > 0))
        {
            return 0;
        }

        var allowed = UnseenPixels / finestPixelsPerPoint;
        var level = 0;
        while (level < Levels && FinestTolerance * (1 << level) <= allowed)
        {
            level++;
        }

        return level;
    }

    /// <summary>The levels of <paramref name="step"/>; null for a step drawn as it is - a fill, a short line, one that
    /// bends, a dashed one (a shorter line would move its dashes), one no level makes lighter. Worked out on the calling
    /// thread.</summary>
    public static PdfStrokeDetail Of(PdfGeometryStep step)
    {
        // Read off the path's own segments: a line or a polyline is straight, any other bends.
        if (step.Fill != null || step.Stroke is not { } pen || pen.Dashes.Count > 0 ||
            step.Geometry is not StreamGeometry { Transform: null } geometry)
        {
            return null;
        }

        var pieces = 0;
        foreach (var figure in geometry.Figures)
        {
            foreach (var segment in figure.Segments)
            {
                switch (segment)
                {
                    case LineSegment:
                        pieces++;
                        break;
                    case PolylineSegment polyline:
                        pieces += polyline.Points.Count;
                        break;
                    default:
                        return null;
                }
            }
        }

        // The tolerance is in the page's points; the line's own units are drawn at the step's scale.
        var scale = LargestScaleOf(step.Matrix);
        if (pieces < FewestPieces || !(scale > 0))
        {
            return null;
        }

        var finest = FinestTolerance / scale;
        var figures = new List<Figure>(geometry.Figures.Count);
        foreach (var figure in geometry.Figures)
        {
            if (figure.Segments.Count == 0)
            {
                continue;
            }

            var points = new List<Vector2<double>> { figure.StartPoint };
            foreach (var segment in figure.Segments)
            {
                if (segment is LineSegment line)
                {
                    points.Add(line.Point);
                }
                else
                {
                    points.AddRange(((PolylineSegment)segment).Points);
                }
            }

            figures.Add(new Figure(points, SignificanceOf(points, finest), figure.IsClosed));
        }

        // A level is counted, not made: a level not worth it draws as the finer one before it does.
        var drawnAs = new int[Levels];
        var finer = 0;
        var keptFiner = pieces + figures.Count;
        for (var level = 1; level <= Levels; level++)
        {
            var tolerance = finest * (1 << (level - 1));
            var kept = 0;
            foreach (var figure in figures)
            {
                kept += figure.KeptAt(tolerance);
            }

            if (kept <= WorthMaking * keptFiner)
            {
                finer = level;
                keptFiner = kept;
            }

            drawnAs[level - 1] = finer;
        }

        return finer == 0 ? null : new PdfStrokeDetail(step, figures, finest, drawnAs);
    }

    /// <summary>The step to draw at <paramref name="level"/> - the line as given at 0 - its copy made the first time it
    /// is asked for: on the loop thread, where the page is drawn.</summary>
    public PdfGeometryStep StepAt(int level)
    {
        var source = level > 0 ? _drawnAs[Math.Min(level, Levels) - 1] : 0;
        if (source == 0)
        {
            return _step;
        }

        return _made[source - 1] ??= new PdfGeometryStep(CopyAt(_figures, _finest * (1 << (source - 1))), null,
            _step.Stroke, _step.Matrix) { IsPagePath = _step.IsPagePath };
    }

    private static StreamGeometry CopyAt(List<Figure> figures, double tolerance)
    {
        var copy = new StreamGeometry();
        var context = copy.Open();
        var kept = new List<Vector2<double>>();
        foreach (var figure in figures)
        {
            figure.Keep(tolerance, kept);
            var segments = context.BeginFigure(kept[0], true, figure.IsClosed);
            kept.RemoveAt(0);
            segments.PolylineLineTo(kept);
            if (figure.IsClosed)
            {
                segments.CloseFigure();
            }
        }

        copy.RecalculateBounds();
        return copy;
    }

    // No less than the most the matrix stretches a length, sheared too: the root of its four squares.
    private static double LargestScaleOf(Matrix4x4F matrix) =>
        Math.Sqrt((double)matrix.M11 * matrix.M11 + (double)matrix.M12 * matrix.M12 +
                  (double)matrix.M21 * matrix.M21 + (double)matrix.M22 * matrix.M22);

    /// <summary>The largest tolerance Douglas - Peucker keeps each point of <paramref name="line"/> at - the ends at
    /// any - so a simplification at a tolerance keeps the points above it: no point it leaves out between two it keeps
    /// is farther than the tolerance from the piece joining them. Points not kept at
    /// <paramref name="finest"/> are not told apart: 0.</summary>
    internal static double[] SignificanceOf(List<Vector2<double>> line, double finest)
    {
        var significance = new double[line.Count];
        if (line.Count == 0)
        {
            return significance;
        }

        significance[0] = significance[^1] = double.PositiveInfinity;
        // A point is kept while it and every split above it are farther than the tolerance.
        var spans = new Stack<(int First, int Last, double Above)>();
        spans.Push((0, line.Count - 1, double.PositiveInfinity));
        while (spans.Count > 0)
        {
            var (first, last, above) = spans.Pop();
            var start = line[first];
            var chord = line[last] - start;
            var lengthSquared = chord.X * chord.X + chord.Y * chord.Y;
            var farthest = -1.0;
            var at = -1;
            for (var i = first + 1; i < last; i++)
            {
                // To the piece, not the line through it: a point past its end - a spur run out and back - is far.
                var off = line[i] - start;
                var along = lengthSquared > 0 ? Math.Clamp((off.X * chord.X + off.Y * chord.Y) / lengthSquared, 0, 1) : 0;
                var away = off - chord * along;
                var distance = Math.Sqrt(away.X * away.X + away.Y * away.Y);
                if (distance > farthest)
                {
                    farthest = distance;
                    at = i;
                }
            }

            if (farthest > finest)
            {
                var kept = Math.Min(farthest, above);
                significance[at] = kept;
                spans.Push((first, at, kept));
                spans.Push((at, last, kept));
            }
        }

        return significance;
    }

    // A figure's points and how much each matters (SignificanceOf).
    private sealed class Figure(List<Vector2<double>> points, double[] significance, bool isClosed)
    {
        public bool IsClosed { get; } = isClosed;

        /// <summary>How many points a copy at <paramref name="tolerance"/> keeps of the figure.</summary>
        public int KeptAt(double tolerance)
        {
            var count = 0;
            foreach (var value in significance)
            {
                if (value > tolerance)
                {
                    count++;
                }
            }

            return ClosesUp(count) ? points.Count : count;
        }

        public void Keep(double tolerance, List<Vector2<double>> kept)
        {
            kept.Clear();
            for (var i = 0; i < points.Count; i++)
            {
                if (significance[i] > tolerance)
                {
                    kept.Add(points[i]);
                }
            }

            if (ClosesUp(kept.Count))
            {
                kept.Clear();
                kept.AddRange(points);
            }
        }

        // A figure the tolerance would close up into a dot, or a stroke along itself, is kept as given: drawn as a
        // dot, it would not be drawn at all. A closed one that comes back to its start counts it twice.
        private bool ClosesUp(int kept)
        {
            var distinct = points[0] == points[^1] ? kept - 1 : kept;
            return distinct < (IsClosed ? 3 : 2);
        }
    }
}
