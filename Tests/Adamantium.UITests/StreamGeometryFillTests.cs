using System.Linq;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Core.Media;
using NUnit.Framework;

namespace Adamantium.UITests;

// Only a stream geometry's filled figures are cut into triangles, as a PathGeometry's are; the others are outlines,
// stroked as given - an open figure stays open. Cutting a page of open lines as one area took seconds and drew nothing.
[TestFixture]
public class StreamGeometryFillTests
{
    [Test]
    public void UnfilledFigures_AreOutlinesOnly()
    {
        var g = new StreamGeometry();
        var context = g.Open();
        for (var k = 0; k < 3; k++)
        {
            context.BeginFigure(new Vector2<double>(0, k * 10), false, false)
                .PolylineLineTo([new Vector2<double>(50, k * 10 + 5), new Vector2<double>(100, k * 10)], true);
        }

        g.ProcessGeometry(GeometryType.Both);

        Assert.Multiple(() =>
        {
            Assert.That(g.Mesh.Points, Is.Empty, "nothing to fill");
            Assert.That(g.Mesh.Contours, Has.Count.EqualTo(3));
            Assert.That(g.Mesh.Contours.All(contour => !contour.IsGeometryClosed), Is.True, "open lines stay open");
        });
    }

    [Test]
    public void AFilledFigure_IsCut_AndAnUnfilledOneBesideIt_IsNot()
    {
        var g = new StreamGeometry();
        var context = g.Open();
        context.BeginFigure(new Vector2<double>(0, 0), true, true)
            .PolylineLineTo([new Vector2<double>(10, 0), new Vector2<double>(10, 10), new Vector2<double>(0, 10)], true);
        context.BeginFigure(new Vector2<double>(50, 0), false, false)
            .PolylineLineTo([new Vector2<double>(60, 10)], true);

        g.ProcessGeometry(GeometryType.Both);

        Assert.Multiple(() =>
        {
            Assert.That(g.Mesh.Points, Is.Not.Empty, "the square is filled");
            Assert.That(g.Mesh.Points.All(p => p.X <= 10), Is.True, "the open line is no part of the fill");
            Assert.That(g.Mesh.Contours.Any(contour => !contour.IsGeometryClosed), Is.True, "the line is still stroked");
        });
    }
}
