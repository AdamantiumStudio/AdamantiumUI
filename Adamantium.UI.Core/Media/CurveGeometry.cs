using System.Collections.Generic;
using System.Linq;
using Adamantium.Mathematics;
using Adamantium.Mathematics.Triangulation;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Core.Media.Curves;

namespace Adamantium.UI.Core.Media;

/// <summary>A geometry that is its curves alone, made by a <see cref="PathCurveBuilder"/> - no figure or segment objects:
/// what glyph outlines are, for the GPU and for a vector writer. Its bounds are its curves' ink; its figures, mesh and
/// area, where they are asked for, are the curves flattened to a hundredth of a unit, filled by its curves' rule.</summary>
internal sealed class CurveGeometry : Geometry
{
    // How far a straight piece of a flattened curve may stray from it, in the geometry's units.
    private const double Flatness = 0.01;
    private const int MostPieces = 64;

    private readonly PathCurves _curves;
    private readonly Rect _bounds;

    public CurveGeometry(PathCurves curves)
    {
        _curves = curves;
        _bounds = InkOf(curves);
    }

    public override Rect Bounds => Transform == null ? _bounds : _bounds.TransformToAABB(Transform.Matrix);

    public override Geometry Clone() => new CurveGeometry(_curves) { Transform = Transform };

    /// <summary>Nothing to work out: the curves' bounds are known as they are made.</summary>
    public override void RecalculateBounds()
    {
    }

    protected internal override FillRule CurveFillRule => _curves.FillRule;

    private protected override bool AppendCurves(PathCurveBuilder builder)
    {
        builder.Append(_curves);
        return true;
    }

    protected internal override List<(Vector2<double>[] Points, bool IsClosed)> FiguresCore() => Flattened();

    internal override (List<Vector2<double>[]> Rings, FillRule Rule) Area()
    {
        var rings = new List<Vector2<double>[]>();
        foreach (var (points, _) in GetFigures())
        {
            if (points.Length >= 3)
            {
                rings.Add(points);
            }
        }

        return (rings, _curves.FillRule);
    }

    protected internal override void ProcessGeometryCore(GeometryType geometryType)
    {
        Mesh.Clear();
        var figures = Flattened();
        if (figures.Count == 0)
        {
            return;
        }

        var contours = figures.Select(figure => new MeshContour(figure.Points, figure.IsClosed)).ToArray();
        Mesh.AddContours(contours);
        if (geometryType == GeometryType.Outlined)
        {
            return;
        }

        var polygon = new Polygon { FillRule = _curves.FillRule };
        polygon.AddContours(contours);
        Mesh.SetPoints(polygon.FillIndirect());
    }

    // The filled figures as points: each bent curve in as many straight pieces as keep within the flatness.
    private List<(Vector2<double>[] Points, bool IsClosed)> Flattened()
    {
        var figures = new List<(Vector2<double>[] Points, bool IsClosed)>();
        var points = new List<Vector2<double>>();
        foreach (var figure in _curves.Figures)
        {
            if (!figure.IsFilled || figure.Count == 0)
            {
                continue;
            }

            points.Clear();
            for (var i = figure.First; i < figure.First + figure.Count; i++)
            {
                var curve = _curves.Curves[i];
                points.Add(curve.Start);
                var bend = (curve.Start - 2 * curve.Control + curve.End).Length() / 4;
                var pieces = bend <= Flatness ? 1 : Math.Min(MostPieces, (int)Math.Ceiling(Math.Sqrt(bend / Flatness)));
                for (var step = 1; step < pieces; step++)
                {
                    points.Add(curve.At((double)step / pieces));
                }
            }

            figures.Add(([.. points], figure.IsClosed));
        }

        return figures;
    }

    // Where the curves' ink reaches: their ends and the turning points between, not their control points.
    private static Rect InkOf(PathCurves curves)
    {
        if (curves.Curves.Count == 0)
        {
            return Rect.Empty;
        }

        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
        foreach (var curve in curves.Curves)
        {
            Take(curve.Start);
            Take(curve.End);
            if (Turn(curve.Start.X, curve.Control.X, curve.End.X) is { } across)
            {
                Take(curve.At(across));
            }

            if (Turn(curve.Start.Y, curve.Control.Y, curve.End.Y) is { } down)
            {
                Take(curve.At(down));
            }
        }

        return new Rect(left, top, right - left, bottom - top);

        void Take(Vector2<double> point)
        {
            left = Math.Min(left, point.X);
            top = Math.Min(top, point.Y);
            right = Math.Max(right, point.X);
            bottom = Math.Max(bottom, point.Y);
        }
    }

    // Where a quadratic's coordinate turns back, strictly inside the curve; null where it does not.
    private static double? Turn(double start, double control, double end)
    {
        var denominator = start - 2 * control + end;
        if (Math.Abs(denominator) < 1e-12)
        {
            return null;
        }

        var t = (start - control) / denominator;
        return t is > 0 and < 1 ? t : null;
    }
}
