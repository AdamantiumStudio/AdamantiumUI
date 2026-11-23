using System;
using System.Collections.Generic;
using System.Threading;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Curves;
using Adamantium.UI.Pdf.Steps;

namespace Adamantium.UI.Pdf;

/// <summary>A long path of a page drawn with fewer points while it is small on screen - a map's lines and areas at its
/// zoom levels: at each level, the path simplified with a coarser tolerance, every point it leaves out within that of
/// the outline it keeps. A fill is simplified as a line is: what it covers moves by no more than the tolerance, and a
/// fill's cost is the pieces of its outline under each pixel, which a whole page shown small crowds by the hundred. How
/// much each point matters is worked out once, where the page is prepared; a level's copy is made when the zoom comes to
/// it, away from the window (<see cref="PrepareAt"/>), or else the first time it is drawn - made where the page is
/// prepared, the copies of every level kept, they cost the collector more than they save. Only a path of straight
/// pieces: one that bends is drawn as given, at every zoom.</summary>
internal sealed class PdfPathDetail
{
    /// <summary>How far from the path the finest level may leave a point out, in the page's points; each coarser level
    /// twice as far.</summary>
    public const double FinestTolerance = 1.0 / 256;

    /// <summary>The levels: the coarsest leaves points out within 1 pt.</summary>
    public const int Levels = 9;

    // How far from the path a point may be left out where the page is drawn, in device pixels.
    private const double UnseenPixels = 0.25;

    // A shorter path costs too little to be worth levels.
    private const int FewestPieces = 256;

    // A level keeping more than this share of the points of the finer one before it draws as that one does.
    private const double WorthMaking = 0.9;

    // The finest level an area is drawn lighter at: zoomed further in, few of its pieces lie under a pixel, and a copy
    // costs more to make and send to the GPU than it saves there.
    private const int FinestForAreas = 5;

    private readonly PdfGeometryStep _step;
    private readonly FillRule _fillRule;
    private readonly List<Figure> _figures;
    private readonly double _finest;

    // For each level, the finer level whose copy it draws - 0, the path as given - and the copies made so far.
    private readonly int[] _drawnAs;
    private readonly PdfGeometryStep[] _made = new PdfGeometryStep[Levels];

    private PdfPathDetail(PdfGeometryStep step, FillRule fillRule, List<Figure> figures, double finest, int[] drawnAs)
    {
        _step = step;
        _fillRule = fillRule;
        _figures = figures;
        _finest = finest;
        _drawnAs = drawnAs;
    }

    /// <summary>Which level a page shown at no more than <paramref name="finestPixelsPerPoint"/> device pixels to its
    /// point draws: the coarsest whose left-out points stay within a quarter of a pixel; 0, the paths as given.</summary>
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

    /// <summary>The levels of <paramref name="step"/>; null for a step drawn as it is - a short path, one that bends, a
    /// dashed line (a shorter line would move its dashes), one no level makes lighter. Worked out on the calling thread.
    /// </summary>
    public static PdfPathDetail Of(PdfGeometryStep step)
    {
        // Read off the path's own segments: a line or a polyline is straight, any other bends.
        if (step.Fill == null && step.Stroke == null || step.Stroke is { Dashes.Count: > 0 } ||
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

        // The tolerance is in the page's points; the path's own units are drawn at the step's scale.
        var scale = LargestScaleOf(step.Matrix);
        if (pieces < FewestPieces || !(scale > 0))
        {
            return null;
        }

        var finest = FinestTolerance / scale;
        var filled = step.Fill != null;
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

            figures.Add(new Figure(points, SignificanceOf(points, finest), figure.IsClosed, filled));
        }

        // A level is counted, not made: a level not worth it draws as the finer one before it does.
        var drawnAs = new int[Levels];
        var finer = 0;
        var keptFiner = pieces + figures.Count;
        for (var level = filled ? FinestForAreas : 1; level <= Levels; level++)
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

        return finer == 0 ? null : new PdfPathDetail(step, geometry.FillRule, figures, finest, drawnAs);
    }

    /// <summary>The step to draw at <paramref name="level"/> - the path as given at 0: its copy made by
    /// <see cref="PrepareAt"/>, or else the first time it is asked for, on the loop thread, where the page is drawn.
    /// </summary>
    public PdfGeometryStep StepAt(int level)
    {
        var source = SourceOf(level);
        if (source == 0)
        {
            return _step;
        }

        var made = Volatile.Read(ref _made[source - 1]);
        if (made != null)
        {
            return made;
        }

        var copy = CopyAt(source);
        return Interlocked.CompareExchange(ref _made[source - 1], copy, null) ?? copy;
    }

    /// <summary>Whether <see cref="StepAt"/> draws <paramref name="level"/> by a copy already made - the path as given,
    /// at a level it is not lighter at.</summary>
    public bool IsMadeAt(int level)
    {
        var source = SourceOf(level);
        return source == 0 || Volatile.Read(ref _made[source - 1]) != null;
    }

    /// <summary>Makes <paramref name="level"/>'s copy on the calling thread, with what drawing it takes - its outline and
    /// its fill's tiles - which its first draw would make on the loop thread, the window standing still for as long.
    /// </summary>
    internal void PrepareAt(int level, CurveTileBuffers buffers)
    {
        var source = SourceOf(level);
        if (source == 0 || Volatile.Read(ref _made[source - 1]) != null)
        {
            return;
        }

        var copy = CopyAt(source);
        copy.Geometry.PrepareForDrawing(_step.Fill, buffers);
        Interlocked.CompareExchange(ref _made[source - 1], copy, null);
    }

    private int SourceOf(int level) => level > 0 ? _drawnAs[Math.Min(level, Levels) - 1] : 0;

    private PdfGeometryStep CopyAt(int source) =>
        new(CopyAt(_finest * (1 << (source - 1))), _step.Fill, _step.Stroke, _step.Matrix)
        {
            IsPagePath = _step.IsPagePath
        };

    private StreamGeometry CopyAt(double tolerance)
    {
        var copy = new StreamGeometry { FillRule = _fillRule };
        var context = copy.Open();
        var kept = new List<Vector2<double>>();
        foreach (var figure in _figures)
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
            var farthest = -1.0;
            var at = -1;
            for (var i = first + 1; i < last; i++)
            {
                // To the piece, not the line through it: a point past its end - a spur run out and back - is far.
                var distance = DistanceToPiece(line[i], line[first], line[last]);
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

    // A figure's points and how much each matters (SignificanceOf). A filled one covers an area, closed or not.
    private sealed class Figure(List<Vector2<double>> points, double[] significance, bool isClosed, bool isFilled)
    {
        // The fewest points that still draw it, by how much they matter - worked out the first time it closes up.
        private readonly int _repeated = points[0] == points[^1] ? 1 : 0;
        private int[] _fewest;

        public bool IsClosed { get; } = isClosed;

        // A figure covering an area takes three points apart; a line, two.
        private int Least => IsClosed || isFilled ? 3 : 2;

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

            return ClosesUp(count) ? Fewest.Length : count;
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

            if (!ClosesUp(kept.Count))
            {
                return;
            }

            // Closed up into a dot, a stroke along itself or an area of nothing, it would not be drawn at all: drawn by
            // its farthest points instead - each left out within the tolerance of them, as the rest are.
            kept.Clear();
            foreach (var index in Fewest)
            {
                kept.Add(points[index]);
            }
        }

        // One that comes back to its start counts it twice.
        private bool ClosesUp(int kept) => kept - _repeated < Least;

        // The fewest points apart that still draw it, in order, as Douglas - Peucker takes them: a line's ends, an area's
        // start and the point farthest from it, then each time the point farthest from the pieces between those kept -
        // round an area, back to its start too. Worked out the first time it closes up.
        private int[] Fewest => _fewest ??= FarthestPoints();

        private int[] FarthestPoints()
        {
            // Round an area, its last point and its first are neighbours, not two ends far apart.
            var ring = IsClosed || isFilled;
            var kept = new List<int> { 0 };
            if (!ring && points.Count > 1)
            {
                kept.Add(points.Count - 1);
            }

            while ((ring ? kept.Count : kept.Count - _repeated) < Least)
            {
                var farthest = 0.0;
                var at = -1;
                var after = 0;
                for (var piece = 0; piece < kept.Count; piece++)
                {
                    var hasNext = piece + 1 < kept.Count;
                    if (!hasNext && !ring && kept.Count > 1)
                    {
                        break;
                    }

                    // The points after this one kept up to the next - or, round an area, to its end and back to the start.
                    var end = hasNext ? kept[piece + 1] : points.Count;
                    var to = points[hasNext ? kept[piece + 1] : kept[0]];
                    for (var i = kept[piece] + 1; i < end; i++)
                    {
                        var distance = DistanceToPiece(points[i], points[kept[piece]], to);
                        if (distance > farthest)
                        {
                            farthest = distance;
                            at = i;
                            after = piece + 1;
                        }
                    }
                }

                // Every point on the pieces kept: nothing more to tell them apart by.
                if (at < 0)
                {
                    break;
                }

                kept.Insert(after, at);
            }

            return kept.ToArray();
        }
    }

    // How far a point lies from the piece between two others, past its ends measured to the nearer.
    private static double DistanceToPiece(Vector2<double> point, Vector2<double> start, Vector2<double> end)
    {
        var chord = end - start;
        var off = point - start;
        var lengthSquared = chord.X * chord.X + chord.Y * chord.Y;
        var along = lengthSquared > 0 ? Math.Clamp((off.X * chord.X + off.Y * chord.Y) / lengthSquared, 0, 1) : 0;
        var away = off - chord * along;
        return Math.Sqrt(away.X * away.X + away.Y * away.Y);
    }
}
