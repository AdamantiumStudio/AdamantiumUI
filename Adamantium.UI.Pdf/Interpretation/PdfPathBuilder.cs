using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Pdf.Interpretation;

/// <summary>The current path (ISO 32000-2, 8.5.2), in user space: subpaths begun by <c>m</c> or <c>re</c> and closed by
/// <c>h</c>. A path that is one rectangle and nothing else is kept as that rectangle - a cut to it can be a scissor.
/// </summary>
internal sealed class PdfPathBuilder
{
    private readonly List<PdfSubpath> _subpaths = [];
    private PdfSubpath _open;
    private Vector2<double> _current;
    private Rect? _rectangle;

    /// <summary>Whether the path has a segment to paint: a lone <c>m</c> paints nothing.</summary>
    public bool IsEmpty
    {
        get
        {
            foreach (var subpath in _subpaths)
            {
                if (subpath.Points.Count > 0)
                {
                    return false;
                }
            }

            return true;
        }
    }

    public bool HasCurrentPoint { get; private set; }

    public void MoveTo(double x, double y)
    {
        _current = new Vector2<double>(x, y);
        _open = new PdfSubpath(_current);
        _subpaths.Add(_open);
        HasCurrentPoint = true;
        _rectangle = null;
    }

    public void LineTo(double x, double y)
    {
        if (!HasCurrentPoint)
        {
            return;
        }

        Reopen();
        _current = new Vector2<double>(x, y);
        _open.Points.Add(_current);
        _open.OnCurve.Add(false);
        _rectangle = null;
    }

    public void CurveTo(double x1, double y1, double x2, double y2, double x3, double y3)
    {
        if (!HasCurrentPoint)
        {
            return;
        }

        Reopen();
        _current = new Vector2<double>(x3, y3);
        _open.Points.Add(new Vector2<double>(x1, y1));
        _open.Points.Add(new Vector2<double>(x2, y2));
        _open.Points.Add(_current);
        _open.OnCurve.Add(true);
        _open.OnCurve.Add(true);
        _open.OnCurve.Add(true);
        _rectangle = null;
    }

    /// <summary><c>v</c>: the first control point is the current point.</summary>
    public void CurveFromCurrent(double x2, double y2, double x3, double y3) =>
        CurveTo(_current.X, _current.Y, x2, y2, x3, y3);

    public void Close()
    {
        if (_open == null)
        {
            return;
        }

        _open.Closed = true;
        _current = _open.Start;
        _open = null;
    }

    public void Rectangle(double x, double y, double width, double height)
    {
        var first = _subpaths.Count == 0;
        MoveTo(x, y);
        LineTo(x + width, y);
        LineTo(x + width, y + height);
        LineTo(x, y + height);
        Close();
        _rectangle = first
            ? new Rect(System.Math.Min(x, x + width), System.Math.Min(y, y + height), System.Math.Abs(width),
                System.Math.Abs(height))
            : null;
    }

    public void Clear()
    {
        _subpaths.Clear();
        _open = null;
        HasCurrentPoint = false;
        _rectangle = null;
    }

    /// <summary>The path as one rectangle, when that is all it is.</summary>
    public Rect? AsRectangle => _rectangle;

    /// <summary>The path as a geometry filled by <paramref name="rule"/>; an open subpath is closed by a fill and left
    /// open by a stroke.</summary>
    public Geometry ToGeometry(FillRule rule)
    {
        if (_rectangle is { } rectangle)
        {
            return new RectangleGeometry(rectangle);
        }

        var geometry = new StreamGeometry { FillRule = rule };
        var context = geometry.Open();
        foreach (var subpath in _subpaths)
        {
            if (subpath.Points.Count == 0)
            {
                continue;
            }

            var figure = context.BeginFigure(subpath.Start, true, subpath.Closed);
            for (var i = 0; i < subpath.Points.Count; i++)
            {
                if (subpath.OnCurve[i])
                {
                    figure.CubicBezierTo(subpath.Points[i], subpath.Points[i + 1], subpath.Points[i + 2]);
                    i += 2;
                }
                else
                {
                    figure.LineTo(subpath.Points[i]);
                }
            }

            if (subpath.Closed)
            {
                figure.CloseFigure();
            }
        }

        geometry.RecalculateBounds();
        return geometry;
    }

    // After h the current point is the subpath's start, and a segment drawn from it begins a new subpath there.
    private void Reopen()
    {
        if (_open != null)
        {
            return;
        }

        _open = new PdfSubpath(_current);
        _subpaths.Add(_open);
    }
}
