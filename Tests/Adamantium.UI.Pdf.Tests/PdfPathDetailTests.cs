using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Media;
using NUnit.Framework;

namespace Adamantium.UI.Pdf.Tests;

/// <summary>A page's long paths - lines and areas - drawn by fewer points at a zoom that cannot show the rest: every point
/// left out stays within a quarter of a pixel of the path drawn, the path as given where it can be seen, and only paths
/// of straight pieces.</summary>
public class PdfPathDetailTests
{
    // Worked out once at the finest, the significance gives every coarser level: each keeps its line within it.
    [TestCase(0.05)]
    [TestCase(0.25)]
    [TestCase(1.0)]
    public void ALineAtALevel_KeepsItsEnds_AndEveryPointLeftOutStaysWithinTheTolerance(double tolerance)
    {
        var random = new Random(7);
        var line = new List<Vector2<double>>();
        double y = 0;
        for (var i = 0; i < 2000; i++)
        {
            y += (random.NextDouble() - 0.5) * 0.4;
            line.Add(new Vector2<double>(i * 0.1, y));
        }

        var kept = KeptAt(line, PdfPathDetail.SignificanceOf(line, 0.01), tolerance);

        var farthest = line.Max(point => DistanceToPolyline(point, kept));
        Assert.Multiple(() =>
        {
            Assert.That(kept[0], Is.EqualTo(line[0]));
            Assert.That(kept[^1], Is.EqualTo(line[^1]));
            Assert.That(kept, Has.Count.LessThan(line.Count * 3 / 4));
            Assert.That(farthest, Is.LessThanOrEqualTo(tolerance + 1e-9));
        });
    }

    [Test]
    public void ASpurRunOutAndBack_IsKept()
    {
        List<Vector2<double>> line = [new(0, 0), new(10, 0), new(5, 0)];

        Assert.That(KeptAt(line, PdfPathDetail.SignificanceOf(line, 0.01), 0.25), Is.EqualTo(line));
    }

    [Test]
    public void ADashedLine_IsDrawnAsGiven_AsAShorterLineWouldMoveItsDashes()
    {
        var page = Prepared(Wiggle(curved: false).Replace("S", "[2 1] 0 d S"));
        var drawn = Session(page.AtDetail(0.5)).All<DrawnGeometry>().Single().Geometry;
        Assert.That(drawn, Is.SameAs(page.CollectPaths().Single().Geometry));
    }

    [Test]
    public void ARingThatComesDownToATriangle_IsDrawnAsOne()
    {
        // 400 points along two sides of a triangle, shaken far less than the tolerance, closed back along the third.
        var text = new StringBuilder("10 10 m ");
        Vector2<double>[] corners = [new(10, 10), new(110, 10), new(60, 90)];
        for (var i = 1; i <= 400; i++)
        {
            var side = (i - 1) / 200;
            var along = ((i - 1) % 200 + 1) / 200.0;
            var point = corners[side] + (corners[side + 1] - corners[side]) * along;
            var shake = i == 200 || i == 400 ? 0 : i % 2 == 0 ? 0.001 : -0.001;
            text.Append(string.Create(CultureInfo.InvariantCulture, $"{point.X} {point.Y + shake} l "));
        }

        var page = Prepared(text.Append("h S").ToString());

        var points = Session(page.AtDetail(0.01)).All<DrawnGeometry>().Single().Geometry.GetFigures().Single().Points;
        Assert.That(points, Has.Length.LessThanOrEqualTo(4));
    }

    [Test]
    public void RingsSmallerThanTheTolerance_AreStillDrawn()
    {
        var text = new StringBuilder();
        for (var ring = 0; ring < 100; ring++)
        {
            var x = 10 + ring;
            text.Append(string.Create(CultureInfo.InvariantCulture,
                $"{x} 50 m {x + 0.05} 50 l {x + 0.05} 50.05 l {x} 50.05 l h "));
        }

        var page = Prepared(text.Append("2 w S").ToString());

        var figures = Session(page.AtDetail(0.01)).All<DrawnGeometry>().Single().Geometry.GetFigures();
        Assert.Multiple(() =>
        {
            Assert.That(figures, Has.Count.EqualTo(100));
            Assert.That(figures.All(figure => figure.Points.Length >= 3), Is.True);
        });
    }

    [TestCase(0.0, 0)]
    [TestCase(-1.0, 0)]
    [TestCase(1e6, 0)]
    [TestCase(0.01, PdfPathDetail.Levels)]
    public void TheLevel_OfAScale(double finestPixelsPerPoint, int level) =>
        Assert.That(PdfPathDetail.LevelFor(finestPixelsPerPoint), Is.EqualTo(level));

    [TestCase(0.5)]
    [TestCase(3)]
    [TestCase(20)]
    public void TheLevel_LeavesPointsOutWithinAQuarterOfAPixel(double finestPixelsPerPoint)
    {
        var level = PdfPathDetail.LevelFor(finestPixelsPerPoint);
        Assume.That(level, Is.GreaterThan(0));
        var tolerance = PdfPathDetail.FinestTolerance * (1 << (level - 1));
        Assert.That(tolerance * finestPixelsPerPoint, Is.LessThanOrEqualTo(0.25));
    }

    [Test]
    public void ALongLine_IsDrawnByFewerPoints_WhereTheyCannotBeSeen_AndAsGivenWhereTheyCan()
    {
        var page = Prepared(Wiggle(curved: false));
        var given = DrawnPoints(page);

        var far = DrawnPoints(page.AtDetail(0.5));
        var near = DrawnPoints(page.AtDetail(1000));

        Assert.Multiple(() =>
        {
            Assert.That(far, Is.LessThan(given / 4), $"of {given}");
            Assert.That(near, Is.EqualTo(given));
            Assert.That(page.AtDetail(0.5).CollectPaths().Single().Geometry.GetFigures().Single().Points,
                Has.Length.EqualTo(given), "a measurement snaps to the line as given");
        });
    }

    // A whole page shown small puts hundreds of an area's pieces under a pixel, each counted where it is drawn.
    [TestCase("f", FillRule.NonZero)]
    [TestCase("f*", FillRule.EvenOdd)]
    public void ALongArea_IsDrawnByFewerPoints_WhereTheyCannotBeSeen_ByItsOwnRuleAndPaint(string fill, FillRule rule)
    {
        var page = Prepared("1 0 0 rg " + Wiggle(curved: false).Replace("S", "100 20 l 10 20 l h " + fill));
        var given = DrawnPoints(page);

        var far = Session(page.AtDetail(0.5)).All<DrawnGeometry>().Single();
        var near = DrawnPoints(page.AtDetail(1000));

        Assert.Multiple(() =>
        {
            Assert.That(far.Geometry.GetFigures().Sum(figure => figure.Points.Length), Is.LessThan(given / 4),
                $"of {given}");
            Assert.That(((StreamGeometry)far.Geometry).FillRule, Is.EqualTo(rule));
            Assert.That(far.Brush, Is.Not.Null);
            Assert.That(far.Pen, Is.Null);
            Assert.That(near, Is.EqualTo(given));
        });
    }

    // An open figure fills the area it would close round: brought down to two points, it would cover nothing. Kept by
    // the three points that matter most, not by all of them - a map shown whole has thousands too small to see.
    [Test]
    public void AreasSmallerThanTheTolerance_AreStillFilled_ClosedOrNot_ByTheirThreeFarthestPoints()
    {
        var text = new StringBuilder();
        for (var spot = 0; spot < 100; spot++)
        {
            var x = 10 + spot;
            for (var corner = 0; corner < 8; corner++)
            {
                var angle = corner * Math.PI / 4;
                var point = string.Create(CultureInfo.InvariantCulture,
                    $"{x + 0.03 * Math.Cos(angle)} {50 + 0.03 * Math.Sin(angle)}");
                text.Append(point).Append(corner == 0 ? " m " : " l ");
            }
        }

        var page = Prepared(text.Append('f').ToString());

        var figures = Session(page.AtDetail(0.01)).All<DrawnGeometry>().Single().Geometry.GetFigures();
        Assert.Multiple(() =>
        {
            Assert.That(figures, Has.Count.EqualTo(100));
            Assert.That(figures.All(figure => figure.Points.Length is >= 3 and <= 4), Is.True);
        });
    }

    // Its farthest points, not the first three: three neighbours along one side would fill a sliver, an area of nothing.
    [Test]
    public void AClosedAreaSmallerThanTheTolerance_IsFilledByItsFarthestPoints()
    {
        // Four squares 0.05 on a side, 40 points along each side, each back at its start - which the simplification
        // brings down to that point alone.
        var text = new StringBuilder();
        for (var square = 0; square < 4; square++)
        {
            var x = 10.0 + square;
            Vector2<double>[] corners = [new(x, 50), new(x + 0.05, 50), new(x + 0.05, 50.05), new(x, 50.05), new(x, 50)];
            for (var side = 0; side < 4; side++)
            {
                for (var step = 0; step < 40; step++)
                {
                    var point = corners[side] + (corners[side + 1] - corners[side]) * (step / 40.0);
                    text.Append(string.Create(CultureInfo.InvariantCulture, $"{point.X} {point.Y}"))
                        .Append(side == 0 && step == 0 ? " m " : " l ");
                }
            }

            text.Append(string.Create(CultureInfo.InvariantCulture, $"{x} 50 l h "));
        }

        var page = Prepared(text.Append('f').ToString());

        var figures = Session(page.AtDetail(0.01)).All<DrawnGeometry>().Single().Geometry.GetFigures();
        Assert.That(figures, Has.Count.EqualTo(4));
        Assert.Multiple(() =>
        {
            foreach (var figure in figures)
            {
                var points = figure.Points;
                var area = 0.0;
                for (var i = 0; i < points.Length; i++)
                {
                    var next = points[(i + 1) % points.Length];
                    area += points[i].X * next.Y - next.X * points[i].Y;
                }

                Assert.That(points, Has.Length.InRange(3, 4));
                Assert.That(Math.Abs(area) / 2, Is.GreaterThan(0.05 * 0.05 / 3), "half the square, give or take");
            }
        });
    }

    // A map's point symbols: a round cap on a line of no length draws a dot.
    [Test]
    public void ADotARoundCapDraws_IsStillDrawn()
    {
        var text = new StringBuilder("1 J 2 w ");
        for (var dot = 0; dot < 300; dot++)
        {
            text.Append(string.Create(CultureInfo.InvariantCulture, $"{10 + dot * 0.5} 50 m {10 + dot * 0.5} 50 l h "));
        }

        var page = Prepared(text.Append('S').ToString());

        var figures = Session(page.AtDetail(0.01)).All<DrawnGeometry>().Single().Geometry.GetFigures();
        Assert.Multiple(() =>
        {
            Assert.That(figures, Has.Count.EqualTo(300));
            Assert.That(figures.All(figure => figure.Points.Length >= 2), Is.True);
        });
    }

    [Test]
    public void AnAreaFilledAndStroked_KeepsBoth_AtALevel()
    {
        var page = Prepared("0 0 1 rg 1 0 0 RG " + Wiggle(curved: false).Replace("S", "100 20 l 10 20 l h B"));

        var drawn = Session(page.AtDetail(0.5)).All<DrawnGeometry>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(drawn.Geometry, Is.Not.SameAs(page.CollectPaths().Single().Geometry), "simplified");
            Assert.That(drawn.Brush, Is.Not.Null);
            Assert.That(drawn.Pen, Is.Not.Null);
        });
    }

    // Made away from the window - the zoom coming to a level - its first draw makes nothing on the loop thread.
    [Test]
    public void ALevelPrepared_IsDrawnByTheCopiesItMade()
    {
        var page = Prepared(Wiggle(curved: false));
        Assume.That(page.IsPreparedAt(0.5), Is.False);

        page.PrepareDetail(0.5);
        var first = Session(page.AtDetail(0.5)).All<DrawnGeometry>().Single().Geometry;
        var again = Session(page.AtDetail(0.5)).All<DrawnGeometry>().Single().Geometry;

        Assert.Multiple(() =>
        {
            Assert.That(page.IsPreparedAt(0.5), Is.True);
            Assert.That(again, Is.SameAs(first));
            Assert.That(page.IsPreparedAt(1000), Is.True, "the paths as given need no copy");
        });
    }

    [Test]
    public void ALineThatBends_IsDrawnAsGiven_AtEveryZoom()
    {
        var page = Prepared(Wiggle(curved: true));
        var drawn = Session(page.AtDetail(0.5)).All<DrawnGeometry>().Single().Geometry;
        Assert.That(drawn, Is.SameAs(page.CollectPaths().Single().Geometry));
    }

    [Test]
    public void BeforeItIsPrepared_ThePageIsDrawnAsGiven()
    {
        var document = new TestPdf().Open(Wiggle(curved: false));
        var page = PdfPageDrawing.Build(document, document.Pages[0]);
        var drawn = Session(page.AtDetail(0.5)).All<DrawnGeometry>().Single().Geometry;
        Assert.That(drawn, Is.SameAs(page.CollectPaths().Single().Geometry));
    }

    private static PdfPageDrawing Prepared(string contents)
    {
        var document = new TestPdf().Open(contents);
        var page = PdfPageDrawing.Build(document, document.Pages[0]);
        page.PrepareForDrawing();
        return page;
    }

    // A line of 600 short pieces across the page, wiggling far less than a point; bent pieces once in a while if asked.
    private static string Wiggle(bool curved)
    {
        var text = new StringBuilder("10 50 m ");
        for (var i = 1; i <= 600; i++)
        {
            var x = 10 + i * 0.3;
            var y = 50 + (i % 2 == 0 ? 0.01 : -0.01);
            text.Append(curved && i % 50 == 0
                ? string.Create(CultureInfo.InvariantCulture, $"{x - 0.2} {y + 5} {x - 0.1} {y + 5} {x} {y} c ")
                : string.Create(CultureInfo.InvariantCulture, $"{x} {y} l "));
        }

        return text.Append('S').ToString();
    }

    private static RecordingSession Session(PdfPageDrawing page)
    {
        var session = new RecordingSession();
        page.Render(session, Matrix4x4<float>.Identity, null);
        return session;
    }

    private static int DrawnPoints(PdfPageDrawing page) =>
        Session(page).All<DrawnGeometry>().Single().Geometry.GetFigures().Sum(figure => figure.Points.Length);

    private static List<Vector2<double>> KeptAt(List<Vector2<double>> line, double[] significance, double tolerance) =>
        line.Where((_, index) => significance[index] > tolerance).ToList();

    private static double DistanceToPolyline(Vector2<double> point, List<Vector2<double>> line)
    {
        var nearest = double.MaxValue;
        for (var i = 1; i < line.Count; i++)
        {
            var start = line[i - 1];
            var piece = line[i] - start;
            var lengthSquared = piece.X * piece.X + piece.Y * piece.Y;
            var along = lengthSquared > 0
                ? Math.Clamp(((point.X - start.X) * piece.X + (point.Y - start.Y) * piece.Y) / lengthSquared, 0, 1)
                : 0;
            var off = point - (start + piece * along);
            nearest = Math.Min(nearest, Math.Sqrt(off.X * off.X + off.Y * off.Y));
        }

        return nearest;
    }
}
