using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Media.Curves;

/// <summary>Gathers a geometry's figures as quadratic curves: straight pieces and quadratics as they are, cubics cut
/// into quadratics that stay within a small part of their size of them - at 6400% a fraction of a pixel.</summary>
internal sealed class PathCurveBuilder
{
    // How far a quadratic may stray from the cubic it stands for, as a part of the cubic's size.
    private const double CubicTolerance = 1.0 / 4096;

    // A cubic is cut in two at most this many times over.
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

    // One quadratic where it stays close enough, else the two halves of the cubic, each again. The quadratic's control
    // is where the cubic's two control legs meet when its ends are held; it strays at most sqrt(3)/36 of the cubic's
    // third difference.
    private void AddCubic(Vector2<double> p0, Vector2<double> p1, Vector2<double> p2, Vector2<double> p3,
        double tolerance, int cuts)
    {
        var third = p3 - p2 * 3 + p1 * 3 - p0;
        if (cuts >= MostCuts || third.Length() * (Math.Sqrt(3) / 36) <= tolerance)
        {
            _curves.Add(new QuadraticCurve(p0, ((p1 + p2) * 3 - p0 - p3) * 0.25, p3));
            return;
        }

        var a = (p0 + p1) * 0.5;
        var b = (p1 + p2) * 0.5;
        var c = (p2 + p3) * 0.5;
        var ab = (a + b) * 0.5;
        var bc = (b + c) * 0.5;
        var middle = (ab + bc) * 0.5;
        AddCubic(p0, a, ab, middle, tolerance, cuts + 1);
        AddCubic(middle, bc, c, p3, tolerance, cuts + 1);
    }

    private static double Size(Vector2<double> p0, Vector2<double> p1, Vector2<double> p2, Vector2<double> p3)
    {
        var width = Math.Max(Math.Max(p0.X, p1.X), Math.Max(p2.X, p3.X)) - Math.Min(Math.Min(p0.X, p1.X), Math.Min(p2.X, p3.X));
        var height = Math.Max(Math.Max(p0.Y, p1.Y), Math.Max(p2.Y, p3.Y)) - Math.Min(Math.Min(p0.Y, p1.Y), Math.Min(p2.Y, p3.Y));
        return Math.Max(width, height);
    }
}
