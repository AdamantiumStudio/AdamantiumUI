using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Curves;
using NUnit.Framework;

namespace Adamantium.XamlTests;

// A geometry's outline as quadratic curves - what the GPU will fill and stroke at any zoom - and the bands a shader walks
// them by: each kind of segment given exactly, a cubic within its tolerance, and the winding counted right.
[TestFixture]
public class PathCurvesTests
{
    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        UIAppContext.Initialize(new FakeApp(new AdamantiumDependencyContainer()), null);
    }

    [Test]
    public void ACubicIsQuadraticsThatStayWithinAPartOfItsSize()
    {
        var p0 = new Vector2<double>(0, 0);
        var p1 = new Vector2<double>(30, 120);
        var p2 = new Vector2<double>(170, -60);
        var p3 = new Vector2<double>(200, 100);
        var curves = Path(f => f.CubicBezierTo(p1, p2, p3), p0).Curves;

        var worst = 0.0;
        for (var i = 0; i <= 400; i++)
        {
            var t = i / 400.0;
            var rest = 1 - t;
            var onCubic = p0 * (rest * rest * rest) + p1 * (3 * rest * rest * t) + p2 * (3 * rest * t * t) + p3 * (t * t * t);
            worst = Math.Max(worst, Nearest(curves, onCubic));
        }

        Assert.That(worst, Is.LessThan(200.0 / 4096 * 1.5), "within a thousandth of the curve's size");
        Assert.That(curves.Count, Is.LessThan(64), "and not a polyline in disguise");
    }

    [Test]
    public void AWholeEllipseLiesOnTheEllipse()
    {
        var ellipse = new EllipseGeometry(new Vector2<double>(50, 40), 30, 20);
        var curves = ellipse.CurvesForDrawing;

        var worst = 0.0;
        foreach (var curve in curves.Curves)
        {
            for (var i = 0; i <= 20; i++)
            {
                var p = curve.At(i / 20.0) - new Vector2<double>(50, 40);
                worst = Math.Max(worst, Math.Abs(Math.Sqrt(p.X * p.X / 900 + p.Y * p.Y / 400) - 1));
            }
        }

        Assert.That(worst, Is.LessThan(4e-4));
        Assert.That(curves.Figures.Single().IsClosed, Is.True);
    }

    [Test]
    public void AnArcEndsWhereItSaysAndRunsAsItsPointsDo()
    {
        var arc = new ArcSegment
        {
            Point = new Vector2<double>(100, 0), Size = new Size<double>(60, 60), SweepDirection = SweepDirection.Clockwise
        };
        var figure = new PathFigure { StartPoint = Vector2<double>.Zero, IsClosed = false };
        figure.Segments.Add(arc);
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);

        var curves = geometry.CurvesForDrawing.Curves;
        var points = arc.ProcessSegment(Vector2<double>.Zero);

        Assert.That(curves[^1].End, Is.EqualTo(new Vector2<double>(100, 0)));
        Assert.That(points.Max(point => Nearest(curves, point)), Is.LessThan(0.01), "the same arc the points walk");
    }

    [Test]
    public void AQuadraticBSplineIsItsSpansExactly()
    {
        var spline = new BSplineSegment([new(40, 80), new(80, 0), new(120, 80)], true);
        var figure = new PathFigure { StartPoint = Vector2<double>.Zero, IsClosed = false };
        figure.Segments.Add(spline);
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);

        var curves = geometry.CurvesForDrawing.Curves;
        var points = spline.ProcessSegment(Vector2<double>.Zero);

        Assert.That(points.Max(point => Nearest(curves, point)), Is.LessThan(1e-3),
            "the points are resampled along chords of the spline, the curves are its spans");
    }

    [Test]
    public void TheGeometrysTransformMovesTheCurves()
    {
        var rectangle = new RectangleGeometry(new Rect(0, 0, 10, 10))
        {
            Transform = new Transform { TranslateX = 100, TranslateY = 50 }
        };

        var bounds = rectangle.CurvesForDrawing.Bounds;

        Assert.That(bounds, Is.EqualTo(new Rect(100, 50, 10, 10)));
    }

    [Test]
    public void ARoundedRectanglesCornersAreQuarterCircles()
    {
        var rectangle = new RectangleGeometry(new Rect(0, 0, 100, 60), new Adamantium.ProceduralGeometry.CornerRadius(20));

        var curves = rectangle.CurvesForDrawing;

        var corner = new Vector2<double>(20, 20);
        var worst = curves.Curves
            .Where(c => c.Start.X <= 20 && c.Start.Y <= 20 && c.End.X <= 20 && c.End.Y <= 20 && c.Start != c.End)
            .SelectMany(c => Enumerable.Range(0, 11).Select(i => (c.At(i / 10.0) - corner).Length()))
            .Max(distance => Math.Abs(distance - 20));
        Assert.That(worst, Is.LessThan(0.02));
    }

    [Test]
    public void EveryPieceRunsOneWayInXAndInY()
    {
        var circle = new EllipseGeometry(new Vector2<double>(0, 0), 50, 50);

        var pieces = CurvePieces.Of(circle.CurvesForDrawing.FillCurves());

        foreach (var piece in pieces.Pieces)
        {
            Assert.That(Between(piece.Control.X, piece.Start.X, piece.End.X) &&
                        Between(piece.Control.Y, piece.Start.Y, piece.End.Y), Is.True, piece.ToString());
        }
    }

    [Test]
    public void TheWindingCountsAsTheFiguresGoRound()
    {
        // Two squares the same way round overlap twice; a third the other way cancels one.
        var geometry = new PathGeometry { FillRule = FillRule.NonZero };
        geometry.Figures.Add(Square(0, 0, 100, clockwise: true));
        geometry.Figures.Add(Square(50, 50, 100, clockwise: true));
        geometry.Figures.Add(Square(60, 60, 20, clockwise: false));

        var pieces = CurvePieces.Of(geometry.CurvesForDrawing.FillCurves());

        Assert.Multiple(() =>
        {
            Assert.That(Math.Abs(pieces.Winding(new(25, 25))), Is.EqualTo(1));
            Assert.That(Math.Abs(pieces.Winding(new(90, 90))), Is.EqualTo(2));
            Assert.That(Math.Abs(pieces.Winding(new(70, 70))), Is.EqualTo(1), "the hole the other way round");
            Assert.That(pieces.Winding(new(200, 10)), Is.Zero);
            Assert.That(pieces.Winding(new(-5, 50)), Is.Zero);
        });
    }

    [Test]
    public void AStarCrossingItselfWindsTwiceInItsMiddle()
    {
        var figure = new PathFigure { StartPoint = new(50, 0) };
        foreach (var point in (Vector2<double>[])[new(79, 90), new(2, 35), new(98, 35), new(21, 90)])
        {
            figure.Segments.Add(new LineSegment { Point = point });
        }

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        var pieces = CurvePieces.Of(geometry.CurvesForDrawing.FillCurves());

        Assert.Multiple(() =>
        {
            Assert.That(Math.Abs(pieces.Winding(new(50, 50))), Is.EqualTo(2), "the middle: even-odd leaves it empty");
            Assert.That(Math.Abs(pieces.Winding(new(50, 15))), Is.EqualTo(1), "a point of the star");
        });
    }

    [Test]
    public void ACircleWindsOnceInsideAndNotOutside()
    {
        var circle = new EllipseGeometry(new Vector2<double>(0, 0), 50, 50);
        var pieces = CurvePieces.Of(circle.CurvesForDrawing.FillCurves());

        var random = new Random(7);
        for (var i = 0; i < 500; i++)
        {
            var p = new Vector2<double>(random.NextDouble() * 140 - 70, random.NextDouble() * 140 - 70);
            var radius = p.Length();
            if (Math.Abs(radius - 50) < 0.05)
            {
                continue;
            }

            Assert.That(Math.Abs(pieces.Winding(p)), Is.EqualTo(radius < 50 ? 1 : 0), p.ToString());
        }
    }

    private static PathCurves Path(Action<PathFigureBuilder> draw, Vector2<double> start)
    {
        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        draw(new PathFigureBuilder(figure));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry.CurvesForDrawing;
    }

    private static PathFigure Square(double x, double y, double size, bool clockwise)
    {
        Vector2<double>[] corners = clockwise
            ? [new(x + size, y), new(x + size, y + size), new(x, y + size)]
            : [new(x, y + size), new(x + size, y + size), new(x + size, y)];
        var figure = new PathFigure { StartPoint = new(x, y) };
        foreach (var corner in corners)
        {
            figure.Segments.Add(new LineSegment { Point = corner });
        }

        return figure;
    }

    private static bool Between(double value, double a, double b) =>
        value >= Math.Min(a, b) - 1e-9 && value <= Math.Max(a, b) + 1e-9;

    private static double Nearest(IReadOnlyList<QuadraticCurve> curves, Vector2<double> point)
    {
        // A coarse walk, then the nearest stretch narrowed down: the distance to a curve, not to its samples.
        var best = double.MaxValue;
        foreach (var curve in curves)
        {
            var at = 0;
            var near = double.MaxValue;
            for (var i = 0; i <= 200; i++)
            {
                var distance = (curve.At(i / 200.0) - point).Length();
                if (distance < near)
                {
                    (near, at) = (distance, i);
                }
            }

            double low = Math.Max(0, at - 1) / 200.0, high = Math.Min(200, at + 1) / 200.0;
            for (var step = 0; step < 60; step++)
            {
                var a = low + (high - low) / 3;
                var b = high - (high - low) / 3;
                if ((curve.At(a) - point).Length() < (curve.At(b) - point).Length())
                {
                    high = b;
                }
                else
                {
                    low = a;
                }
            }

            best = Math.Min(best, Math.Min(near, (curve.At((low + high) / 2) - point).Length()));
        }

        return best;
    }

    private sealed class PathFigureBuilder(PathFigure figure)
    {
        public void CubicBezierTo(Vector2<double> c1, Vector2<double> c2, Vector2<double> end) =>
            figure.Segments.Add(new CubicBezierSegment(c1, c2, end, true));
    }
}
