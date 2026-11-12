using System;
using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Curves;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A stroke runs along the geometry's curves, not along its mesh: a bending contour keeps its pieces' controls,
/// a straight one none, and how far along it each point lies is the curve's own length.</summary>
[TestFixture]
public class StrokeContourTests
{
    [Test]
    public void ACircle_IsOneClosedContourOfBendingPieces_OnTheCircle()
    {
        var circle = new EllipseGeometry(new Vector2<double>(10, 20), 50, 50);

        var contours = circle.StrokeForDrawing.Contours;

        Assert.That(contours, Has.Count.EqualTo(1));
        var contour = contours[0];
        Assert.Multiple(() =>
        {
            Assert.That(contour.IsClosed, Is.True);
            Assert.That(contour.Controls, Is.Not.Null);
            Assert.That(contour.Controls, Has.Length.EqualTo(contour.Points.Length));
            var worst = Enumerable.Range(0, contour.PieceCount)
                .SelectMany(i => Enumerable.Range(0, 9).Select(k => contour.Piece(i).At(k / 8.0)))
                .Max(p => Math.Abs((p - new Vector2<double>(10, 20)).Length() - 50));
            Assert.That(worst, Is.LessThan(0.01), "every piece runs on the circle");
        });
    }

    [Test]
    public void ACirclesLength_IsItsCircumference()
    {
        var contour = new EllipseGeometry(new Vector2<double>(0, 0), 50, 50).StrokeForDrawing.Contours[0];

        var arcs = contour.Arcs(out var length);

        Assert.Multiple(() =>
        {
            Assert.That(arcs[0], Is.Zero);
            Assert.That(length, Is.EqualTo(Math.Tau * 50).Within(Math.Tau * 50 * 1e-4));
            for (var i = 1; i < arcs.Length; i++) Assert.That(arcs[i], Is.GreaterThan(arcs[i - 1]));
        });
    }

    [Test]
    public void ARectangle_IsStraight_WithNoControls()
    {
        var rectangle = new RectangleGeometry(new Rect(0, 0, 100, 60));

        var contour = rectangle.StrokeForDrawing.Contours.Single();
        contour.Arcs(out var length);

        Assert.Multiple(() =>
        {
            Assert.That(contour.IsClosed, Is.True);
            Assert.That(contour.Controls, Is.Null);
            Assert.That(contour.Points, Has.Length.EqualTo(4));
            Assert.That(length, Is.EqualTo(320).Within(1e-9));
        });
    }

    [Test]
    public void AnOpenCubic_EndsWhereItEnds_AsLongAsItIs()
    {
        var figure = new PathFigure { StartPoint = new Vector2<double>(0, 0), IsClosed = false };
        figure.Segments.Add(new CubicBezierSegment(new Vector2<double>(0, 100), new Vector2<double>(200, -100),
            new Vector2<double>(200, 0), true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);

        var contour = geometry.StrokeForDrawing.Contours.Single();
        contour.Arcs(out var length);

        // The cubic's own length, summed finely along it.
        var fine = 0.0;
        var last = new Vector2<double>(0, 0);
        for (var k = 1; k <= 20000; k++)
        {
            var t = k / 20000.0;
            var rest = 1 - t;
            var p = new Vector2<double>(0, 0) * (rest * rest * rest) + new Vector2<double>(0, 100) * (3 * rest * rest * t) +
                    new Vector2<double>(200, -100) * (3 * rest * t * t) + new Vector2<double>(200, 0) * (t * t * t);
            fine += (p - last).Length();
            last = p;
        }

        Assert.Multiple(() =>
        {
            Assert.That(contour.IsClosed, Is.False);
            Assert.That(contour.Points[0], Is.EqualTo(new Vector2<double>(0, 0)));
            Assert.That((contour.Points[^1] - new Vector2<double>(200, 0)).Length(), Is.LessThan(1e-9));
            Assert.That(length, Is.EqualTo(fine).Within(fine * 1e-4));
        });
    }

    // A piece bent hard enough for five Gauss points to miss its length is halved until they do not.
    [Test]
    public void AHardBend_IsMeasuredAsCloselyAsAGentleOne()
    {
        var figure = new PathFigure { StartPoint = new Vector2<double>(0, 0), IsClosed = false };
        figure.Segments.Add(new QuadraticBezierSegment
        {
            ControlPoint = new Vector2<double>(1000, 1), Point = new Vector2<double>(0, 2)
        });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);

        var contour = geometry.StrokeForDrawing.Contours.Single();
        contour.Arcs(out var length);

        var curve = new QuadraticCurve(new Vector2<double>(0, 0), new Vector2<double>(1000, 1), new Vector2<double>(0, 2));
        var fine = 0.0;
        var last = curve.Start;
        for (var k = 1; k <= 200000; k++)
        {
            var p = curve.At(k / 200000.0);
            fine += (p - last).Length();
            last = p;
        }

        Assert.Multiple(() =>
        {
            Assert.That(contour.PieceCount, Is.GreaterThan(1), "halved");
            Assert.That(length, Is.EqualTo(fine).Within(fine * 1e-4));
        });
    }

    [Test]
    public void LengthTo_OfAStraightPiece_IsItsDistance()
    {
        var line = QuadraticCurve.Line(new Vector2<double>(1, 2), new Vector2<double>(4, 6));

        Assert.Multiple(() =>
        {
            Assert.That(line.LengthTo(1), Is.EqualTo(5).Within(1e-12));
            Assert.That(line.LengthTo(0.5), Is.EqualTo(2.5).Within(1e-12));
            Assert.That(line.IsStraight(), Is.True);
        });
    }
}
