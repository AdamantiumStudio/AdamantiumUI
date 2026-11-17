using System.Runtime.InteropServices;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Rendering;
using Adamantium.UI.Rendering.Payloads;
using Adamantium.UI.Rendering.Retained;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

/// <summary>What keeps a leaned map's frame cheap: a geometry given its triangles - a map's tile - fills from them under
/// one color, not from curves the shader walks at every pixel; another brush keeps its curves; a stroke record keeps its
/// size with the feather it now carries under a perspective.</summary>
[TestFixture]
public class LeanedMapDrawingTests
{
    private static readonly Vector2<double>[] Square = [new(0, 0), new(10, 0), new(10, 10), new(0, 10)];

    [Test]
    public void AGeometryGivenItsTriangles_FillsFromThem_NotFromCurves()
    {
        var tile = new MeshGeometry(Square, [0, 1, 2, 2, 3, 0], [Square]);
        tile.ProcessGeometry(ProceduralGeometry.GeometryType.Both);

        var payload = new GeometryPayload(Brushes.Red, tile);
        var frozen = FrozenMesh.From(tile, payload.Outline, payload.FillsFromTriangles);

        Assert.Multiple(() =>
        {
            Assert.That(payload.FillsFromTriangles, Is.True);
            Assert.That(payload.Outline, Is.Null, "no curves are made for it");
            Assert.That(frozen, Is.Not.Null);
            Assert.That(frozen.Curves, Is.Null, "it is drawn from its triangles");
        });
    }

    // Only one color fills from triangles: another brush's edge has no ring, and the curves keep it smooth.
    [Test]
    public void AGeometryGivenItsTriangles_UnderAGradient_KeepsItsCurves()
    {
        var tile = new MeshGeometry(Square, [0, 1, 2, 2, 3, 0], [Square]);
        tile.ProcessGeometry(ProceduralGeometry.GeometryType.Both);
        var gradient = new LinearGradientBrush();
        gradient.GradientStops.Add(new GradientStop(Colors.Red, 0));
        gradient.GradientStops.Add(new GradientStop(Colors.Blue, 1));

        var payload = new GeometryPayload(gradient, tile);

        Assume.That(InstancedFillCollector.Enabled && InstancedFillCollector.CurvesEnabled);
        Assert.Multiple(() =>
        {
            Assert.That(payload.FillsFromTriangles, Is.False);
            Assert.That(payload.Outline, Is.Not.Null);
        });
    }

    [Test]
    public void APath_StillFillsFromItsCurves()
    {
        var path = new RectangleGeometry(new Rect(0, 0, 10, 10));
        var payload = new GeometryPayload(Brushes.Red, path);

        Assume.That(InstancedFillCollector.Enabled && InstancedFillCollector.CurvesEnabled);
        Assert.That(payload.Outline, Is.Not.Null);
    }

    [Test]
    public void AStrokeRecord_KeepsItsSize_WithTheFeatherItCarries()
    {
        // 64 for the matrix, 48 for the addresses, 32 for the counts and the feather, 64 for the four vectors.
        Assert.That(Marshal.SizeOf<StrokeRecord>(), Is.EqualTo(208));
    }
}
