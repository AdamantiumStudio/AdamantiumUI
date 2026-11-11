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

    /// <summary>Bands each way at most in <see cref="FillBands"/>; a fill of more pieces has fuller bands.</summary>
    public const int MostBands = 64;

    private CurveBands _fillBands;

    /// <summary>The fill's curves cut and banded for the shader - made once, by whichever thread asks first.</summary>
    public CurveBands FillBands
    {
        get
        {
            if (_fillBands is { } made)
            {
                return made;
            }

            Interlocked.CompareExchange(ref _fillBands, CurveBands.Of(FillCurves(), MostBands), null);
            return _fillBands;
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
    /// start where it does not end there.</summary>
    public List<QuadraticCurve> FillCurves()
    {
        var fill = new List<QuadraticCurve>(Curves.Count + Figures.Count);
        foreach (var figure in Figures)
        {
            if (!figure.IsFilled || figure.Count == 0)
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
