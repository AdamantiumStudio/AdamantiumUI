using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Media.Curves;

/// <summary>A geometry's outline as quadratic curves, its <see cref="Geometry.Transform"/> applied: what the GPU fills
/// and strokes exactly at any zoom, rather than the polyline its mesh is cut into.</summary>
internal sealed class PathCurves
{
    public PathCurves(IReadOnlyList<QuadraticCurve> curves, IReadOnlyList<CurveFigure> figures, FillRule fillRule,
        int version)
    {
        Curves = curves;
        Figures = figures;
        FillRule = fillRule;
        Version = version;
        Bounds = BoundsOf(curves);
    }

    /// <summary>Which points the figures enclose.</summary>
    public FillRule FillRule { get; }

    private CurvePieces _fillPieces;

    /// <summary>The fill's curves cut into pieces for the shader - made once, by whichever thread asks first.</summary>
    public CurvePieces FillPieces
    {
        get
        {
            if (_fillPieces is { } made)
            {
                return made;
            }

            Interlocked.CompareExchange(ref _fillPieces, CurvePieces.Of(FillCurves()), null);
            return _fillPieces;
        }
    }

    private CurveTileTree _fillTiles;

    /// <summary>The fill's pieces in a tree of its own tiles (<see cref="CurveTileTree"/>) - made once, by whichever
    /// thread asks first; null for a fill of no area.</summary>
    public CurveTileTree FillTiles
    {
        get
        {
            if (_fillTiles is { } made)
            {
                return made;
            }

            var fill = FillPieces;
            if (fill.Pieces.Length == 0 || !(fill.Bounds.Width > 0) || !(fill.Bounds.Height > 0))
            {
                return null;
            }

            var tiles = CurveTileTree.Of(fill.Pieces, new Vector2<double>(fill.Bounds.X, fill.Bounds.Y),
                new Vector2<double>(fill.Bounds.Width, fill.Bounds.Height));
            Interlocked.CompareExchange(ref _fillTiles, tiles, null);
            return _fillTiles;
        }
    }

    /// <summary>Every curve, the figures' one after another.</summary>
    public IReadOnlyList<QuadraticCurve> Curves { get; }

    /// <summary>The figures, in the order given.</summary>
    public IReadOnlyList<CurveFigure> Figures { get; }

    /// <summary>The geometry's version these were made from.</summary>
    public int Version { get; }

    /// <summary>What the curves and their controls cover; holds the curves.</summary>
    public Rect Bounds { get; }

    /// <summary>The curves a fill is made of: those of the filled figures, each closed by a straight piece back to its
    /// start where it does not end there. A figure with a coordinate not a finite number is left out, not the fill.
    /// </summary>
    public List<QuadraticCurve> FillCurves()
    {
        var fill = new List<QuadraticCurve>(Curves.Count + Figures.Count);
        foreach (var figure in Figures)
        {
            if (!figure.IsFilled || figure.Count == 0 || !IsFinite(figure))
            {
                continue;
            }

            for (var i = figure.First; i < figure.First + figure.Count; i++)
            {
                fill.Add(Curves[i]);
            }

            var start = Curves[figure.First].Start;
            var end = Curves[figure.First + figure.Count - 1].End;
            if (start != end)
            {
                fill.Add(QuadraticCurve.Line(end, start));
            }
        }

        return fill;
    }

    private bool IsFinite(CurveFigure figure)
    {
        for (var i = figure.First; i < figure.First + figure.Count; i++)
        {
            if (!Curves[i].IsFinite)
            {
                return false;
            }
        }

        return true;
    }

    private const int LoopSteps = 4;

    /// <summary>The filled figures as closed loops of points - each bent curve as a few straight pieces - or null when
    /// there are none. New arrays each call.</summary>
    public List<(Vector2<double>[] Points, bool IsClosed)> FillLoops()
    {
        List<(Vector2<double>[] Points, bool IsClosed)> loops = null;
        var points = new List<Vector2<double>>();
        foreach (var figure in Figures)
        {
            if (!figure.IsFilled || figure.Count == 0 || !IsFinite(figure))
            {
                continue;
            }

            points.Clear();
            for (var i = figure.First; i < figure.First + figure.Count; i++)
            {
                var curve = Curves[i];
                points.Add(curve.Start);
                if (!curve.IsStraight())
                {
                    for (var step = 1; step < LoopSteps; step++)
                    {
                        points.Add(curve.At((double)step / LoopSteps));
                    }
                }
            }

            if (points.Count >= 3)
            {
                (loops ??= []).Add((points.ToArray(), true));
            }
        }

        return loops;
    }

    private static Rect BoundsOf(IReadOnlyList<QuadraticCurve> curves)
    {
        if (curves.Count == 0)
        {
            return Rect.Empty;
        }

        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
        foreach (var curve in curves)
        {
            foreach (var point in (ReadOnlySpan<Vector2<double>>)[curve.Start, curve.Control, curve.End])
            {
                left = Math.Min(left, point.X);
                top = Math.Min(top, point.Y);
                right = Math.Max(right, point.X);
                bottom = Math.Max(bottom, point.Y);
            }
        }

        return new Rect(left, top, right - left, bottom - top);
    }
}
