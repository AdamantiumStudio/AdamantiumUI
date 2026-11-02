using System.Linq;
using System.Threading.Tasks;
using Adamantium.Core.TypeParsing;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Collections;
using Adamantium.UI.Core.Media;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A polyline geometry is its lines and rings as given, for a pen: open lines stay open, rings close, nothing
/// is cut into triangles; from code or markup, made on whatever thread.</summary>
[TestFixture]
public class PolylineGeometryTests
{
    private static readonly Vector2<double>[] Line = [new(0, 0), new(10, 5), new(20, 0)];
    private static readonly Vector2<double>[] Ring = [new(30, 0), new(40, 0), new(40, 10)];

    [Test]
    public void LinesAndRings_AreTheContours_AndNothingIsFilled()
    {
        var geometry = new PolylineGeometry([Line], [Ring]);

        geometry.ProcessGeometry(GeometryType.Both);

        Assert.Multiple(() =>
        {
            Assert.That(geometry.Mesh.Points, Is.Empty);
            Assert.That(geometry.Mesh.Contours, Has.Count.EqualTo(2));
            Assert.That(geometry.Mesh.Contours[0].IsGeometryClosed, Is.False);
            Assert.That(geometry.Mesh.Contours[1].IsGeometryClosed, Is.True);
            Assert.That(geometry.Bounds, Is.EqualTo(new Rect(0, 0, 40, 10)));
        });
    }

    [Test]
    public void TooShortContours_AreLeftOut()
    {
        var geometry = new PolylineGeometry([[new Vector2<double>(1, 1)]], [[new(0, 0), new(1, 1)]]);

        geometry.ProcessGeometry(GeometryType.Both);

        Assert.Multiple(() =>
        {
            Assert.That(geometry.Mesh.Contours, Is.Empty);
            Assert.That(geometry.Bounds, Is.EqualTo(Rect.Empty));
        });
    }

    [Test]
    public void ANewTransform_MovesTheBounds()
    {
        var geometry = new PolylineGeometry([Line]) { Transform = new Transform { TranslateX = 5 } };
        _ = geometry.Bounds;

        geometry.Transform = new Transform { TranslateX = 50 };

        Assert.That(geometry.Bounds, Is.EqualTo(new Rect(50, 0, 20, 5)));
    }

    [Test]
    public void AChangedCollection_IsStrokedAnew()
    {
        var geometry = new PolylineGeometry([Line]);
        geometry.ProcessGeometry(GeometryType.Both);
        var before = geometry.Version;

        geometry.Lines.Add(new PointsCollection([new(0, 20), new(5, 30)]));
        geometry.ProcessGeometry(GeometryType.Both);

        Assert.Multiple(() =>
        {
            Assert.That(geometry.Version, Is.GreaterThan(before));
            Assert.That(geometry.Mesh.Contours, Has.Count.EqualTo(2));
            Assert.That(geometry.Bounds, Is.EqualTo(new Rect(0, 0, 20, 30)));
        });
    }

    [Test]
    public void TheBounds_AreTransformedWithTheLines()
    {
        var geometry = new PolylineGeometry([Line]) { Transform = new Transform { TranslateX = 5 } };

        Assert.That(geometry.Bounds, Is.EqualTo(new Rect(5, 0, 20, 5)));
    }

    [Test]
    public void FromMarkup_LinesAndRingsParse()
    {
        var geometry = new PolylineGeometry
        {
            Lines = TypeParser.Parse<ContourCollection>("0,0 10,5 20,0; 0,20 5,30"),
            Rings = TypeParser.Parse<ContourCollection>("30,0 40,0 40,10"),
        };

        geometry.ProcessGeometry(GeometryType.Both);

        Assert.That(geometry.Mesh.Contours.Count(contour => !contour.IsGeometryClosed), Is.EqualTo(2));
    }

    [Test]
    public async Task MadeOnAnotherThread_IsStrokedAsWell()
    {
        var geometry = await Task.Run(() => new PolylineGeometry([Line], [Ring]));

        geometry.ProcessGeometry(GeometryType.Both);

        Assert.That(geometry.Mesh.Contours, Has.Count.EqualTo(2));
    }
}
