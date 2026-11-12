using System.Linq;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Core.Media;
using NUnit.Framework;

namespace Adamantium.UITests;

// A geometry's contours are what its stroke runs along: each figure once, every point as given.
[TestFixture]
public class GeometryStrokeContourTests
{
    static Vector2<double> V(double x, double y) => new(x, y);

    static readonly Vector2<double>[] Pinched = [V(0, 0), V(120, 0), V(60, 60), V(120, 120), V(0, 120), V(60, 60)];

    static readonly Vector2<double>[] Square = [V(0, 0), V(10, 0), V(10, 10), V(0, 10)];

    static PathGeometry Path(Vector2<double>[] points)
    {
        var figure = new PathFigure { StartPoint = points[0], IsClosed = true, IsFilled = true, Segments = new PathSegmentCollection() };
        for (var i = 1; i < points.Length; i++)
        {
            figure.Segments.Add(new LineSegment(points[i], true));
        }

        return new PathGeometry { Figures = new PathFigureCollection { figure } };
    }

    [Test]
    public void AStreamRingThroughAPointTwice_IsStrokedThroughItTwice()
    {
        var g = new StreamGeometry();
        g.Open().BeginFigure(Pinched[0], true, true).PolylineLineTo(Pinched.Skip(1).ToArray(), true);

        g.ProcessGeometry(GeometryType.Both);

        Assert.That(g.Mesh.Contours.Single().Points, Is.EqualTo(Pinched));
    }

    [Test]
    public void APathRingThroughAPointTwice_IsStrokedThroughItTwice()
    {
        var g = Path(Pinched);

        g.ProcessGeometry(GeometryType.Both);

        Assert.That(g.Mesh.Contours.Single().Points, Is.EqualTo(Pinched));
    }

    [Test]
    public void AFilledPathFigure_IsOneContour()
    {
        var g = Path(Square);

        g.ProcessGeometry(GeometryType.Both);

        Assert.That(g.Mesh.Contours, Has.Count.EqualTo(1));
    }

    [Test]
    public void AFilledPath_InAnEvenOddGroup_IsFilled()
    {
        var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
        group.Children.Add(Path(Square));

        group.ProcessGeometry(GeometryType.Both);

        Assert.That(group.Mesh.Points, Is.Not.Empty);
    }
}
