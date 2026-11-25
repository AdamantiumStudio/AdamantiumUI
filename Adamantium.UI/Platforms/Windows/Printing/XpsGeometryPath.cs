using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Graphics.Vector;
using Adamantium.Win32.Printing.Xps;

namespace Adamantium.UI.Platforms.Windows.Printing;

/// <summary>A path gathered into an XPS geometry: figures of lines and quadratic and cubic Béziers, each segment
/// stroked, every figure filled. Built with <see cref="ToGeometry"/>.</summary>
internal sealed class XpsGeometryPath : IVectorPath
{
    private readonly List<Figure> _figures = [];
    private Figure _current;

    private sealed class Figure(Vector2<double> start)
    {
        public Vector2<double> Start { get; } = start;

        public List<XpsSegmentType> Types { get; } = [];

        public List<float> Data { get; } = [];

        public bool Closed { get; set; }
    }

    /// <summary>Whether any figure has been begun.</summary>
    public bool IsEmpty => _figures.Count == 0;

    public void MoveTo(Vector2<double> point)
    {
        _current = new Figure(point);
        _figures.Add(_current);
    }

    public void LineTo(Vector2<double> point)
    {
        var figure = Current(point);
        figure.Types.Add(XpsSegmentType.Line);
        figure.Data.Add((float)point.X);
        figure.Data.Add((float)point.Y);
    }

    public void QuadraticTo(Vector2<double> control, Vector2<double> end)
    {
        var figure = Current(control);
        figure.Types.Add(XpsSegmentType.QuadraticBezier);
        figure.Data.Add((float)control.X);
        figure.Data.Add((float)control.Y);
        figure.Data.Add((float)end.X);
        figure.Data.Add((float)end.Y);
    }

    public void CubicTo(Vector2<double> first, Vector2<double> second, Vector2<double> end)
    {
        var figure = Current(first);
        figure.Types.Add(XpsSegmentType.Bezier);
        figure.Data.Add((float)first.X);
        figure.Data.Add((float)first.Y);
        figure.Data.Add((float)second.X);
        figure.Data.Add((float)second.Y);
        figure.Data.Add((float)end.X);
        figure.Data.Add((float)end.Y);
    }

    public void Close()
    {
        if (_current != null)
        {
            _current.Closed = true;
        }
    }

    /// <summary>The figures as an XPS geometry under <paramref name="evenOdd"/> or the nonzero rule; null when there are
    /// none.</summary>
    public IXpsOMGeometry ToGeometry(IXpsOMObjectFactory factory, bool evenOdd)
    {
        if (_figures.Count == 0)
        {
            return null;
        }

        Check(factory.CreateGeometry(out var geometry));
        Check(geometry.SetFillRule(evenOdd ? XpsFillRule.EvenOdd : XpsFillRule.NonZero));
        Check(geometry.GetFigures(out var figures));
        try
        {
            foreach (var figure in _figures)
            {
                Check(factory.CreateGeometryFigure(new XpsPoint((float)figure.Start.X, (float)figure.Start.Y),
                    out var made));
                try
                {
                    var strokes = new int[figure.Types.Count];
                    Array.Fill(strokes, 1);
                    Check(made.SetSegments((uint)figure.Types.Count, (uint)figure.Data.Count, [.. figure.Types],
                        [.. figure.Data], strokes));
                    Check(made.SetIsClosed(figure.Closed ? 1 : 0));
                    Check(made.SetIsFilled(1));
                    Check(figures.Append(made));
                }
                finally
                {
                    Marshal.ReleaseComObject(made);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(figures);
        }

        return geometry;
    }

    /// <summary>A segment drawn with no figure begun starts one where it starts, as a path does.</summary>
    private Figure Current(Vector2<double> point)
    {
        if (_current == null)
        {
            MoveTo(point);
        }

        return _current;
    }

    internal static void Check(int result) => Marshal.ThrowExceptionForHR(result);
}
