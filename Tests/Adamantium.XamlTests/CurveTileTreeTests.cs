using System;
using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Curves;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A tile's winding is handed down from its parent's along a path inside the parent; it must be what counting
/// every piece at its point gives - for every tile, however deep - and every tile must list each piece passing near it.
/// </summary>
[TestFixture]
public class CurveTileTreeTests
{
    private static IEnumerable<TestCaseData> Fills()
    {
        yield return new TestCaseData(Star(7, FillRule.EvenOdd)).SetName("a seven-pointed star");
        yield return new TestCaseData(Ring()).SetName("a ring with a hole and curved lobes");
        yield return new TestCaseData(Coast()).SetName("a jagged polygon of 4000 lines");
    }

    [TestCaseSource(nameof(Fills))]
    public void EveryTilesWinding_IsWhatCountingEveryPieceGives(Geometry geometry)
    {
        var fill = geometry.CurvesForDrawing.FillPieces;
        var tree = geometry.CurvesForDrawing.FillTiles;
        var corner = new Vector2<double>(fill.Bounds.X, fill.Bounds.Y);

        var wrong = 0;
        for (var t = 0; t < tree.TileCount; t++)
        {
            var at = corner + new Vector2<double>(tree.Points[t * 2], tree.Points[t * 2 + 1]);
            if (fill.Winding(at) != tree.Windings[t]) wrong++;
        }

        Assert.Multiple(() =>
        {
            Assert.That(tree.TileCount, Is.GreaterThan(1), "the tree is cut");
            Assert.That(wrong, Is.Zero, $"of {tree.TileCount} tiles");
        });
    }

    private static Geometry Star(int points, FillRule rule)
    {
        var g = new StreamGeometry { FillRule = rule };
        var corners = new List<Vector2<double>>();
        for (var i = 1; i < points; i++)
        {
            var a = Math.Tau * i * 2 / points;
            corners.Add(new Vector2<double>(100 + Math.Cos(a) * 90, 100 + Math.Sin(a) * 90));
        }

        g.Open().BeginFigure(new Vector2<double>(190, 100), true, true).PolylineLineTo(corners, true);
        return g;
    }

    private static Geometry Ring()
    {
        var ring = new StreamGeometry { FillRule = FillRule.EvenOdd };
        var context = ring.Open();
        context.BeginFigure(new Vector2<double>(160, 20), true, true)
            .ArcTo(new Vector2<double>(160, 300), new Size<double>(140, 140), 0, false, SweepDirection.Clockwise)
            .ArcTo(new Vector2<double>(160, 20), new Size<double>(140, 140), 0, false, SweepDirection.Clockwise);
        context.BeginFigure(new Vector2<double>(160, 90), true, true)
            .CubicBezierTo(new Vector2<double>(260, 60), new Vector2<double>(250, 260), new Vector2<double>(160, 230))
            .CubicBezierTo(new Vector2<double>(40, 200), new Vector2<double>(90, 80), new Vector2<double>(160, 90));
        return ring;
    }

    private static Geometry Coast()
    {
        var random = new Random(11);
        var points = new Vector2<double>[4000];
        var r = 400.0;
        for (var i = 0; i < points.Length; i++)
        {
            r = Math.Clamp(r + (random.NextDouble() - 0.5) * 24, 250, 450);
            var a = Math.Tau * i / points.Length;
            points[i] = new Vector2<double>(500 + Math.Cos(a) * r, 500 + Math.Sin(a) * r);
        }

        var g = new StreamGeometry();
        g.Open().BeginFigure(points[0], true, true).PolylineLineTo(points[1..], true);
        return g;
    }
}
