using System;
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

    // A mesh with its edge given is processed on the thread that cut it, so its fill is made ready there.
    [Test]
    public void AGivenEdge_IsMadeReadyForDrawing_WhenProcessed()
    {
        var mesh = new MeshGeometry(Square, [0, 1, 2, 2, 3, 0], [Square]);

        mesh.ProcessGeometry(GeometryType.Both);

        Assert.Multiple(() =>
        {
            Assert.That(mesh.FillForDrawing, Is.SameAs(mesh.FillForDrawing));
            Assert.That(mesh.FillForDrawing.Ring, Has.Length.EqualTo(4));
        });
    }

    // A frozen mesh keeps only what is drawn: the fill made ready, its edge, its bounds.
    [Test]
    public void AFrozenMesh_LetsGoOfWhatItWasGiven_AndIsDrawnAsBefore()
    {
        var mesh = new MeshGeometry(Square, [0, 1, 2, 2, 3, 0], [Square]);
        mesh.ProcessGeometry(GeometryType.Both);
        var fill = mesh.FillForDrawing;
        var bounds = mesh.Bounds;
        var version = mesh.Version;

        mesh.Freeze();

        Assert.Multiple(() =>
        {
            Assert.That(mesh.IsFrozen);
            Assert.That(mesh.Positions, Is.Null);
            Assert.That(mesh.TriangleIndices, Is.Null);
            Assert.That(mesh.Boundary, Is.Null);
            Assert.That(mesh.Mesh.Points, Is.Empty);
            Assert.That(mesh.Mesh.Contours, Has.Count.EqualTo(1), "the edge stays, for a pen");
            Assert.That(mesh.Version, Is.EqualTo(version));
            Assert.That(mesh.FillForDrawing, Is.SameAs(fill));
            Assert.That(mesh.Bounds, Is.EqualTo(bounds));
        });
    }

    [Test]
    public void AFrozenMesh_IsMadeReadyFirst_WhenItWasNot()
    {
        var mesh = new MeshGeometry(Square, [0, 1, 2, 2, 3, 0], [Square]);

        mesh.Freeze();

        Assert.That(mesh.FillForDrawing.Vertices, Has.Length.EqualTo(4));
    }

    // Without a boundary the fill is made from the mesh when drawn, so the mesh stays.
    [Test]
    public void AFrozenMeshWithoutABoundary_KeepsItsMesh()
    {
        var mesh = new MeshGeometry(Square, [0, 1, 2, 2, 3, 0]);

        mesh.Freeze();

        Assert.Multiple(() =>
        {
            Assert.That(mesh.Positions, Is.Null);
            Assert.That(mesh.Mesh.Points, Has.Length.EqualTo(4));
            Assert.That(mesh.FillForDrawing, Is.Not.Null);
        });
    }

    [Test]
    public void AFrozenMesh_IsNotChanged_NorCopied()
    {
        var mesh = new MeshGeometry(Square, [0, 1, 2, 2, 3, 0], [Square]);
        mesh.Freeze();

        Assert.Multiple(() =>
        {
            Assert.Throws<InvalidOperationException>(() => mesh.Positions = new PointsCollection(Square));
            Assert.Throws<InvalidOperationException>(() => mesh.CutBox = new Rect(0, 0, 5, 5));
            Assert.Throws<InvalidOperationException>(() => mesh.Transform = new Transform { TranslateX = 5 });
            Assert.Throws<InvalidOperationException>(() => mesh.Clone());
            Assert.That(mesh.Positions, Is.Null, "refused before it was written");
            Assert.That(mesh.Transform, Is.Null);
            Assert.That(mesh.IsProcessed, Is.True);
        });
    }

    // Refused before it is written: the mesh keeps nothing of a value it would not take.
    [Test]
    public void AValueAFrozenMeshRefuses_IsNotKept()
    {
        var mesh = new MeshGeometry(Square, [0, 1, 2, 2, 3, 0], [Square]);
        mesh.Freeze();

        var refused = Refuse(mesh);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.That(refused.IsAlive, Is.False);
        GC.KeepAlive(mesh);

        static WeakReference Refuse(MeshGeometry frozen)
        {
            var points = new PointsCollection(Square);
            Assert.Throws<InvalidOperationException>(() => frozen.Positions = points);
            return new WeakReference(points);
        }
    }

    [Test]
    public void AFrozenMesh_TakesTheSameValueAgain()
    {
        var mesh = new MeshGeometry(Square, [0, 1, 2, 2, 3, 0], [Square]) { CutBox = new Rect(0, 0, 10, 10) };
        mesh.Freeze();

        Assert.DoesNotThrow(() =>
        {
            mesh.Positions = null;
            mesh.CutBox = new Rect(0, 0, 10, 10);
            mesh.Transform = null;
        });
    }

    // A transform it shares with another geometry still moves the other one.
    [Test]
    public void ATransformAFrozenMeshShares_StillMovesTheOther()
    {
        var move = new Transform { TranslateX = 5 };
        var frozen = new MeshGeometry(Square, [0, 1, 2, 2, 3, 0], [Square]) { Transform = move };
        frozen.Freeze();
        var other = new MeshGeometry(Square, [0, 1, 2, 2, 3, 0], [Square]) { Transform = move };
        other.ProcessGeometry(GeometryType.Both);
        var version = other.Version;

        move.TranslateX = 20;

        Assert.That(other.Version, Is.GreaterThan(version));
    }

    // Someone listening to it - a binding - does not make freezing undo what was made ready.
    [Test]
    public void AMeshListenedTo_IsFrozenWithItsFillKept()
    {
        var mesh = new MeshGeometry(Square, [0, 1, 2, 2, 3, 0], [Square]);
        mesh.PropertyChanged += (_, _) => { };
        mesh.ProcessGeometry(GeometryType.Both);
        var fill = mesh.FillForDrawing;

        mesh.Freeze();

        Assert.Multiple(() =>
        {
            Assert.That(mesh.IsProcessed, Is.True);
            Assert.That(mesh.FillForDrawing, Is.SameAs(fill));
        });
    }

    // A change of its transform's is refused by the mesh - the transform tells it of the change and is told no - so what
    // was made ready is drawn on as it was, never made again of what it let go of.
    [Test]
    public void AFrozenMesh_KeepsWhatItDraws_WhenItsTransformChanges()
    {
        var move = new Transform { TranslateX = 5 };
        var mesh = new MeshGeometry(Square, [0, 1, 2, 2, 3, 0], [Square]) { Transform = move };
        mesh.Freeze();
        var bounds = mesh.Bounds;
        var fill = mesh.FillForDrawing;
        var version = mesh.Version;

        move.TranslateX = 20;

        Assert.Multiple(() =>
        {
            Assert.That(mesh.IsProcessed, Is.True);
            Assert.That(mesh.Version, Is.EqualTo(version));
            Assert.That(mesh.FillForDrawing, Is.SameAs(fill));
            Assert.That(mesh.Bounds, Is.EqualTo(bounds));
        });
    }

    [Test]
    public void AChangedMesh_IsNotDrawnFromTheFillMadeBefore()
    {
        var mesh = new MeshGeometry(Square, [0, 1, 2, 2, 3, 0], [Square]);
        mesh.ProcessGeometry(GeometryType.Both);
        var before = mesh.FillForDrawing;

        mesh.Positions = new PointsCollection([new(0, 0), new(20, 0), new(20, 20), new(0, 20)]);
        mesh.ProcessGeometry(GeometryType.Both);

        Assert.Multiple(() =>
        {
            Assert.That(mesh.FillForDrawing, Is.Not.SameAs(before));
            Assert.That(mesh.FillForDrawing.Version, Is.EqualTo(mesh.Version));
            Assert.That(mesh.FillForDrawing.Bounds.Width, Is.EqualTo(20));
        });
    }

    // A piece cut from a larger area - a map tile - is not antialiased where it was cut, so pieces side by side meet
    // without a seam.
    [Test]
    public void TheEdgeAlongTheCutBox_IsNotAntialiased()
    {
        var mesh = new MeshGeometry(Square, [0, 1, 2, 2, 3, 0], [Square]) { CutBox = new Rect(0, 0, 10, 20) };

        mesh.ProcessGeometry(GeometryType.Both);

        // Of the square's four sides, the top (y = 0), left and right lie on the box; only the bottom is the area's own.
        var ring = mesh.FillForDrawing.Ring;
        Assert.Multiple(() =>
        {
            Assert.That(ring, Has.Length.EqualTo(1));
            Assert.That(ring[0].A.Y, Is.EqualTo(10));
            Assert.That(ring[0].B.Y, Is.EqualTo(10));
        });
    }

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
    public void TheBounds_HoldTheBoundaryToo()
    {
        Vector2<double>[] wider = [new(-5, 2), new(20, 2), new(20, 30)];
        var mesh = new MeshGeometry(Square, [0, 1, 2, 2, 3, 0], [Square, wider]);

        Assert.That(mesh.Bounds, Is.EqualTo(new Rect(-5, 0, 25, 30)));
    }

    [Test]
    public void TheBoundsOfAPolyline_HoldItsLinesAndRings()
    {
        Vector2<double>[] line = [new(1, 1), new(4, -3)];
        Vector2<double>[] ring = [new(-2, 0), new(0, 8), new(3, 2)];
        var lines = new PolylineGeometry([line], [ring]);

        Assert.That(lines.Bounds, Is.EqualTo(new Rect(-2, -3, 6, 11)));
    }

    [Test]
    public void TwoRectangles_MergeIntoTheOneHoldingBoth()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new Rect(0, 0, 2, 2).Merge(new Rect(5, -1, 1, 1)), Is.EqualTo(new Rect(0, -1, 6, 3)));
            Assert.That(Rect.FromPoints([new Vector2<double>(3, 4), new(-1, 6), new(2, -2)]),
                Is.EqualTo(new Rect(-1, -2, 4, 8)));
            Assert.That(Rect.FromPoints(System.Linq.Enumerable.Repeat(new Vector2<double>(7, 7), 3)),
                Is.EqualTo(new Rect(7, 7, 0, 0)));
            Assert.That(Rect.FromPoints(System.Array.Empty<Vector2<double>>()), Is.EqualTo(Rect.Empty));
        });
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
