using System;
using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Media.Fill;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

/// <summary>Which contours of a fill are holes - inside an odd number of the others - found the same however many there
/// are: a map tile's thousands of buildings, a frame round all of them, holes in holes.</summary>
public class FringeNestingTests
{
    private static Vector2<double>[] Square(double x, double y, double size) =>
        [new(x, y), new(x + size, y), new(x + size, y + size), new(x, y + size)];

    private static bool Inside(Vector2<double> p, Vector2<double>[] poly)
    {
        var inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) &&
                p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    [TestCase(1)]
    [TestCase(40)]
    [TestCase(3000)]
    public void EveryContour_IsAHoleExactlyWhenAnOddNumberHoldIt(int blocks)
    {
        var random = new Random(blocks);
        var contours = new List<(Vector2<double>[] Points, bool IsClosed)> { (Square(-10, -10, 1020), true) };
        for (var b = 0; b < blocks; b++)
        {
            var x = random.NextDouble() * 980;
            var y = random.NextDouble() * 980;
            var size = 2 + random.NextDouble() * 18;
            contours.Add((Square(x, y, size), true));
            if (random.Next(3) == 0) contours.Add((Square(x + size / 4, y + size / 4, size / 2), true));
        }

        AssertNestedAsByEveryPair(contours);
    }

    // Every contour on one line has no height to divide: one row of cells, and the same answer.
    [Test]
    public void ContoursAllOnOneLine_AreNestedTheSame()
    {
        var contours = new List<(Vector2<double>[] Points, bool IsClosed)>();
        for (var i = 0; i < 50; i++)
        {
            contours.Add(([new(i * 10, 0), new(i * 10 + 5, 0), new(i * 10 + 2, 0)], true));
        }

        contours.Add(([new(0, 0), new(1000, 0), new(500, 0)], true));
        AssertNestedAsByEveryPair(contours);
    }

    // A contour of nothing but NaN holds nothing and is held by nothing, and does not upset the others.
    [Test]
    public void AContourOfNaN_UpsetsNothing()
    {
        var nan = new Vector2<double>(double.NaN, double.NaN);
        var contours = new List<(Vector2<double>[] Points, bool IsClosed)>
        {
            (Square(0, 0, 100), true), ([nan, nan, nan], true), (Square(10, 10, 20), true)
        };
        for (var i = 0; i < 40; i++) contours.Add((Square(40 + i, 40, 0.5), true));

        AssertNestedAsByEveryPair(contours);
    }

    // Strips each across a fifth of the plane would crowd the grid as full as asking every contour: answered by asking
    // every contour, the same.
    [Test]
    public void ManyMiddlingStrips_AreNestedTheSame()
    {
        var contours = new List<(Vector2<double>[] Points, bool IsClosed)>();
        for (var i = 0; i < 400; i++)
        {
            var x = i * 2.0;
            contours.Add(([new(x, 0), new(x + 200, 0), new(x + 200, 200), new(x, 200)], true));
        }

        AssertNestedAsByEveryPair(contours);
    }

    private static void AssertNestedAsByEveryPair(List<(Vector2<double>[] Points, bool IsClosed)> contours)
    {
        var built = FringeGeometry.Build(contours);

        Assert.That(built, Has.Count.EqualTo(contours.Count));
        for (var i = 0; i < built.Count; i++)
        {
            var probe = built[i].Points[0];
            var holders = 0;
            for (var j = 0; j < built.Count; j++)
            {
                if (j != i && Inside(probe, built[j].Points)) holders++;
            }

            var outward = SignOfArea(built[i].Points) >= 0 ? -1f : 1f;
            var expected = (holders & 1) == 1 ? -outward : outward;
            Assert.That(built[i].Winding, Is.EqualTo(expected), $"contour {i}, held by {holders}");
        }
    }

    private static double SignOfArea(Vector2<double>[] points)
    {
        var area = 0.0;
        for (var i = 0; i < points.Length; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Length];
            area += a.X * b.Y - b.X * a.Y;
        }

        return area;
    }
}
