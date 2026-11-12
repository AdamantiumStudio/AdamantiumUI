using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Media.Curves;

/// <summary>Gathers a geometry's figures as quadratic curves: straight pieces and quadratics as they are, cubics cut
/// into quadratics that stay within a thousandth of their size of them - within a pixel while a cubic spans under a
/// thousand pixels.</summary>
internal sealed class PathCurveBuilder
{
    private const double CubicTolerance = 1.0 / 1024;
    private const int MostCuts = 12;

    private readonly List<QuadraticCurve> _curves = [];
    private readonly List<CurveFigure> _figures = [];
    private int _first = -1;
    private bool _filled;
    private Vector2<double> _current;

    /// <summary>Where the figure being given has got to.</summary>
    public Vector2<double> Current => _current;

    /// <summary>Starts a figure at <paramref name="start"/>, ending the one before as open.</summary>
    public void BeginFigure(Vector2<double> start, bool filled)
    {
        if (_first >= 0)
        {
            EndFigure(false);
        }

        _first = _curves.Count;
        _filled = filled;
        _current = start;
    }

    /// <summary>Ends the figure being given; <paramref name="closed"/> says whether its outline goes back to its start.
    /// </summary>
    public void EndFigure(bool closed)
    {
        if (_first < 0)
        {
            return;
        }

        var count = _curves.Count - _first;
        if (closed && count > 0 && _curves[_first].Start != _current)
        {
            _curves.Add(QuadraticCurve.Line(_current, _curves[_first].Start));
            count++;
        }

        _figures.Add(new CurveFigure(_first, count, closed, _filled));
        _first = -1;
    }

    public void LineTo(Vector2<double> point)
    {
        if (point != _current)
        {
            _curves.Add(QuadraticCurve.Line(_current, point));
        }

        _current = point;
    }

    public void QuadraticTo(Vector2<double> control, Vector2<double> point)
    {
        _curves.Add(new QuadraticCurve(_current, control, point));
        _current = point;
    }

    public void CubicTo(Vector2<double> control1, Vector2<double> control2, Vector2<double> point)
    {
        var tolerance = Math.Max(Size(_current, control1, control2, point), double.Epsilon) * CubicTolerance;
        AddCubic(_current, control1, control2, point, tolerance, 0);
        _current = point;
    }

    /// <summary>Takes in the figures of <paramref name="curves"/> as they are - a group's children, each made already.
    /// </summary>
    public void Append(PathCurves curves)
    {
        EndFigure(false);
        foreach (var figure in curves.Figures)
        {
            _figures.Add(figure with { First = _curves.Count });
            for (var i = figure.First; i < figure.First + figure.Count; i++)
            {
                _curves.Add(curves.Curves[i]);
            }
        }
    }

    /// <summary>What was given, its figures closed, put through <paramref name="matrix"/> when there is one.</summary>
    public PathCurves Build(int version, Matrix4x4<double>? matrix, FillRule fillRule)
    {
        EndFigure(false);
        if (matrix is { } m && !m.IsIdentity)
        {
            for (var i = 0; i < _curves.Count; i++)
            {
                _curves[i] = _curves[i].Transformed(m);
            }
        }

        return new PathCurves(_curves.ToArray(), _figures.ToArray(), fillRule, version);
    }

    private void AddCubic(Vector2<double> start, Vector2<double> control1, Vector2<double> control2,
        Vector2<double> end, double tolerance, int cuts)
    {
        var thirdDifference = end - control2 * 3 + control1 * 3 - start;
        if (cuts >= MostCuts || thirdDifference.Length() * (Math.Sqrt(3) / 36) <= tolerance)
        {
            _curves.Add(new QuadraticCurve(start, ((control1 + control2) * 3 - start - end) * 0.25, end));
            return;
        }

        var startSide = (start + control1) * 0.5;
        var between = (control1 + control2) * 0.5;
        var endSide = (control2 + end) * 0.5;
        var firstHalfControl = (startSide + between) * 0.5;
        var secondHalfControl = (between + endSide) * 0.5;
        var middle = (firstHalfControl + secondHalfControl) * 0.5;
        AddCubic(start, startSide, firstHalfControl, middle, tolerance, cuts + 1);
        AddCubic(middle, secondHalfControl, endSide, end, tolerance, cuts + 1);
    }

    private static double Size(Vector2<double> start, Vector2<double> control1, Vector2<double> control2,
        Vector2<double> end)
    {
        var right = Math.Max(Math.Max(start.X, control1.X), Math.Max(control2.X, end.X));
        var left = Math.Min(Math.Min(start.X, control1.X), Math.Min(control2.X, end.X));
        var bottom = Math.Max(Math.Max(start.Y, control1.Y), Math.Max(control2.Y, end.Y));
        var top = Math.Min(Math.Min(start.Y, control1.Y), Math.Min(control2.Y, end.Y));
        return Math.Max(right - left, bottom - top);
    }
}
