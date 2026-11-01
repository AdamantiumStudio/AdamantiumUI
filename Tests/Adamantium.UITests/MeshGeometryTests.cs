using System.Threading.Tasks;
using Adamantium.Core.TypeParsing;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Collections;
using Adamantium.UI.Core.Media;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A mesh geometry draws the triangles it is given as they are - nothing re-cut - from code or from markup, on
/// whatever thread it was made; triangles it cannot draw make it draw nothing rather than garbage.</summary>
[TestFixture]
public class MeshGeometryTests
{
    private static readonly Vector2<double>[] Square = [new(0, 0), new(10, 0), new(10, 10), new(0, 10)];

    [Test]
    public void TheTrianglesGiven_AreTheMesh()
    {
        var mesh = new MeshGeometry(Square, [0, 1, 2, 2, 3, 0]);

        mesh.ProcessGeometry(GeometryType.Both);

        Assert.Multiple(() =>
        {
            Assert.That(mesh.Mesh.Points, Has.Length.EqualTo(4));
            Assert.That(mesh.Mesh.Indices, Is.EqualTo(new[] { 0, 1, 2, 2, 3, 0 }));
            Assert.That(mesh.Bounds, Is.EqualTo(new Rect(0, 0, 10, 10)));
            Assert.That(mesh.IsEmpty(), Is.False);
        });
    }

    [TestCase(new[] { 0, 1 }, TestName = "TrianglesItCannotDraw_DrawNothing_NotAMultipleOfThree")]
    [TestCase(new[] { 0, 1, 4 }, TestName = "TrianglesItCannotDraw_DrawNothing_AnIndexPastThePositions")]
    [TestCase(new[] { 0, -1, 2 }, TestName = "TrianglesItCannotDraw_DrawNothing_ANegativeIndex")]
    public void TrianglesItCannotDraw_DrawNothing(int[] indices)
    {
        var mesh = new MeshGeometry(Square, indices);

        mesh.ProcessGeometry(GeometryType.Both);

        Assert.That(mesh.IsEmpty(), Is.True);
    }

    [Test]
    public void AChangedCollection_IsDrawnAnew()
    {
        var mesh = new MeshGeometry(Square, [0, 1, 2]);
        mesh.ProcessGeometry(GeometryType.Both);
        var before = mesh.Version;

        mesh.TriangleIndices.AddRange([2, 3, 0]);
        mesh.Positions.Add(new Vector2<double>(20, 20));
        mesh.ProcessGeometry(GeometryType.Both);

        Assert.Multiple(() =>
        {
            Assert.That(mesh.Version, Is.GreaterThan(before));
            Assert.That(mesh.Mesh.Indices, Has.Length.EqualTo(6));
            Assert.That(mesh.Bounds, Is.EqualTo(new Rect(0, 0, 20, 20)));
        });
    }

    [Test]
    public void AnotherCollection_IsFollowedInstead()
    {
        var mesh = new MeshGeometry(Square, [0, 1, 2]);
        var old = mesh.TriangleIndices;
        mesh.TriangleIndices = new IndexCollection([0, 1, 2, 2, 3, 0]);
        mesh.ProcessGeometry(GeometryType.Both);
        var version = mesh.Version;

        old.Add(3);

        Assert.That(mesh.Version, Is.EqualTo(version), "the collection let go of changes nothing");
    }

    // What markup says reaches the parsers the generated code calls.
    [Test]
    public void FromMarkup_PositionsAndIndicesParse()
    {
        var mesh = new MeshGeometry
        {
            Positions = TypeParser.Parse<PointsCollection>("0,0 10,0 10,10 0,10"),
            TriangleIndices = TypeParser.Parse<IndexCollection>("0 1 2, 2 3 0"),
        };

        mesh.ProcessGeometry(GeometryType.Both);

        Assert.Multiple(() =>
        {
            Assert.That(mesh.TriangleIndices, Is.EqualTo(new[] { 0, 1, 2, 2, 3, 0 }));
            Assert.That(mesh.Mesh.Points, Has.Length.EqualTo(4));
        });
    }

    // Made where it was cut - a worker thread - and drawn on the render thread.
    [Test]
    public async Task MadeOnAnotherThread_IsDrawnAsWell()
    {
        var mesh = await Task.Run(() => new MeshGeometry(Square, [0, 1, 2, 2, 3, 0]));

        mesh.ProcessGeometry(GeometryType.Both);

        Assert.That(mesh.Mesh.Indices, Has.Length.EqualTo(6));
    }

    // The edge given is the mesh's outline: transformed with it, and what a pen draws.
    [Test]
    public void AGivenBoundary_IsTheOutline()
    {
        var mesh = new MeshGeometry(Square, [0, 1, 2, 2, 3, 0], [Square])
        {
            Transform = new Transform { TranslateX = 5 },
        };

        mesh.ProcessGeometry(GeometryType.Outlined);

        Assert.Multiple(() =>
        {
            Assert.That(mesh.Mesh.Contours, Has.Count.EqualTo(1));
            Assert.That(mesh.Mesh.Contours[0].IsGeometryClosed, Is.True);
            Assert.That(mesh.Mesh.Contours[0].Points[0], Is.EqualTo(new Vector2<double>(5, 0)));
            Assert.That(mesh.Mesh.Points, Is.Empty, "an outline has no triangles");
        });
    }

    [Test]
    public void TheBounds_AreTransformedWithTheMesh()
    {
        var mesh = new MeshGeometry(Square, [0, 1, 2, 2, 3, 0]) { Transform = new Transform { TranslateX = 5 } };

        mesh.ProcessGeometry(GeometryType.Both);

        Assert.That(mesh.Bounds, Is.EqualTo(new Rect(5, 0, 10, 10)));
    }

    [Test]
    public void FromMarkup_TheBoundaryParses_RingsApartBySemicolons()
    {
        var boundary = TypeParser.Parse<ContourCollection>("0,0 10,0 10,10 0,10; 2,2 2,8 8,8 8,2");

        Assert.Multiple(() =>
        {
            Assert.That(boundary, Has.Count.EqualTo(2));
            Assert.That(boundary[1], Has.Count.EqualTo(4));
            Assert.That(boundary[1][0], Is.EqualTo(new Vector2<double>(2, 2)));
        });
    }

    // A ring that is no ring once its repeats are gone is no edge: the edge is then worked out of the triangles.
    [Test]
    public void ABoundaryOfNoRealRing_IsNoBoundary()
    {
        Vector2<double>[] flat = [new(0, 0), new(10, 0), new(0, 0)];
        var mesh = new MeshGeometry(Square, [0, 1, 2, 2, 3, 0], [flat]);

        mesh.ProcessGeometry(GeometryType.Both);

        Assert.That(mesh.ContoursAreFillBoundary, Is.False);
    }

    // With no edge given there is no outline to test against: the triangles are the fill, and a hole is not in it.
    [Test]
    public void APath_IsHitInTheTriangles_NotInTheHole()
    {
        var frame = new MeshGeometry(
            [new(0, 0), new(120, 0), new(120, 120), new(0, 120), new(40, 40), new(80, 40), new(80, 80), new(40, 80)],
            [0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5, 2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7]);
        var path = new Adamantium.UI.Controls.Shapes.Path { Data = frame, Fill = Brushes.Red };
        frame.ProcessGeometry(GeometryType.Both);

        Assert.Multiple(() =>
        {
            Assert.That(path.HitTestCore(new Vector2<double>(20, 20)), Is.True, "on the frame");
            Assert.That(path.HitTestCore(new Vector2<double>(60, 60)), Is.False, "in the hole");
        });
    }

    [Test]
    public void AClone_IsTheSameMesh()
    {
        var mesh = new MeshGeometry(Square, [0, 1, 2, 2, 3, 0]);

        var clone = (MeshGeometry)mesh.Clone();
        mesh.Positions[0] = new Vector2<double>(5, 5);

        Assert.That(clone.Positions[0], Is.EqualTo(new Vector2<double>(0, 0)), "the clone has its own positions");
    }
}
