using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using NUnit.Framework;

namespace Adamantium.UI.Pdf.Tests;

/// <summary>The page, its paths, pens and clips, as the draws they become.</summary>
public class PdfPageDrawingTests
{
    internal static (PdfPageDrawing Page, RecordingSession Session) Draw(TestPdf pdf, string contents,
        PdfPageDrawingOptions options = null)
    {
        var document = pdf.Open(contents);
        var page = PdfPageDrawing.Build(document, document.Pages[0], options);
        var session = new RecordingSession();
        page.Render(session, Matrix4x4<float>.Identity, null);
        Assert.That(session.Depth, Is.Zero, "every group is closed");
        var crop = (RectangleGeometry)session.UnwrapPage();
        Assert.That(crop.Rect, Is.EqualTo(page.Bounds), "cut to the crop box");
        return (page, session);
    }

    internal static (double X, double Y) At(Matrix4x4<float> m, double x, double y) =>
        (x * m.M11 + y * m.M21 + m.M41, x * m.M12 + y * m.M22 + m.M42);

    /// <summary>Where the point (<paramref name="x"/>, <paramref name="y"/>) of a path's user space lands on the page,
    /// the path's own corner - a rectangle's, or where its first figure begins - having stood at
    /// (<paramref name="cornerX"/>, <paramref name="cornerY"/>) in it: the interpreter places a path by a corner of a
    /// grid and moves its points by the rest.</summary>
    internal static (double X, double Y) At(Geometry geometry, Matrix4x4<float> m, double x, double y,
        double cornerX = 0, double cornerY = 0)
    {
        var corner = geometry is RectangleGeometry box
            ? new Vector2<double>(box.Rect.X, box.Rect.Y)
            : ((StreamGeometry)geometry).Figures[0].StartPoint;
        return At(m, x - cornerX + corner.X, y - cornerY + corner.Y);
    }

    internal static (double X, double Y) At(DrawnGeometry drawn, double x, double y, double cornerX = 0,
        double cornerY = 0) => At(drawn.Geometry, drawn.Transform, x, y, cornerX, cornerY);

    // A figure's points from where it begins.
    private static Vector2<double>[] FromStart(Vector2<double> start, PathSegment segment) =>
        ((PolylineSegment)segment).Points.Select(point => point - start).ToArray();

    [Test]
    public void ThePageIsInItsPointsYDownFromTheTopLeft()
    {
        var (page, session) = Draw(new TestPdf(), "0 0 10 20 re f");
        Assert.That((page.Bounds.Width, page.Bounds.Height), Is.EqualTo((200.0, 100.0)));
        var drawn = session.All<DrawnGeometry>().Single();
        Assert.That(drawn.Geometry, Is.InstanceOf<RectangleGeometry>());
        Assert.That(At(drawn, 0, 0), Is.EqualTo((0.0, 100.0)));
        Assert.That(At(drawn, 10, 20), Is.EqualTo((10.0, 80.0)));
    }

    // The page's top left corner, (0, 100) in its own space: turned clockwise it goes to the top right, then the bottom
    // right, then the bottom left.
    [TestCase(90, 100, 200, 0, 100, 100, 0)]
    [TestCase(180, 200, 100, 0, 100, 200, 100)]
    [TestCase(270, 100, 200, 0, 100, 0, 200)]
    public void ARotatedPageTurnsClockwise(int rotate, double width, double height, double x, double y,
        double expectedX, double expectedY)
    {
        var (page, session) = Draw(new TestPdf { PageExtra = $"/Rotate {rotate}" }, "0 0 1 1 re f");
        Assert.That((page.Bounds.Width, page.Bounds.Height), Is.EqualTo((width, height)));
        var drawn = session.All<DrawnGeometry>().Single();
        var (ax, ay) = At(drawn, x, y);
        Assert.That(ax, Is.EqualTo(expectedX).Within(1e-4));
        Assert.That(ay, Is.EqualTo(expectedY).Within(1e-4));
    }

    // The paths a page draws are given out - outline, place, stroked or filled - for a measurement to snap to.
    [Test]
    public void ThePathsDrawnAreGivenOut_WithWhereTheyStand()
    {
        var document = new TestPdf().Open("0 0 10 20 re f 5 5 m 50 5 l S");
        var paths = PdfPageDrawing.Build(document, document.Pages[0]).CollectPaths();

        Assert.Multiple(() =>
        {
            Assert.That(paths, Has.Count.EqualTo(2));
            Assert.That(paths[0].IsFilled && !paths[0].IsStroked, Is.True, "the box is filled only");
            Assert.That(paths[1].IsStroked && !paths[1].IsFilled, Is.True, "the line is stroked only");
            Assert.That(At(paths[0].Geometry, paths[0].Matrix, 0, 0), Is.EqualTo((0.0, 100.0)),
                "not in the page's points, y down");
        });
    }

    [Test]
    public void TheCropBoxIsThePage()
    {
        var (page, session) = Draw(new TestPdf { PageExtra = "/CropBox [50 20 150 80]" }, "50 20 1 1 re f");
        Assert.That((page.Bounds.Width, page.Bounds.Height), Is.EqualTo((100.0, 60.0)));
        Assert.That(At(session.All<DrawnGeometry>().Single(), 50, 20, 50, 20), Is.EqualTo((0.0, 60.0)));
    }

    [Test]
    public void AFillTakesItsColorAndAStrokeItsPen()
    {
        var (_, session) = Draw(new TestPdf(),
            "1 0 0 rg 0 0 1 RG 4 w 1 J 2 j [3 2] 1 d 0 0 m 10 10 l 20 0 l B");
        var drawn = session.All<DrawnGeometry>().Single();
        Assert.That(((SolidColorBrush)drawn.Brush).Color, Is.EqualTo(new Color(255, 0, 0, 255)));
        var pen = drawn.Pen;
        Assert.That(((SolidColorBrush)pen.Brush).Color, Is.EqualTo(new Color(0, 0, 255, 255)));
        Assert.That(pen.Thickness, Is.EqualTo(4));
        Assert.That(pen.StartLineCap, Is.EqualTo(PenLineCap.ConvexRound));
        Assert.That(pen.DashStartCap, Is.EqualTo(PenLineCap.ConvexRound));
        Assert.That(pen.PenLineJoin, Is.EqualTo(PenLineJoin.Bevel));
        Assert.That(pen.DashStrokeArray.ToArray(), Is.EqualTo(new[] { 3.0, 2 }));
        Assert.That(pen.DashOffset, Is.EqualTo(1));
        Assert.That(drawn.Geometry, Is.InstanceOf<StreamGeometry>());
    }

    [Test]
    public void AStrokesMiterLimitIsItsPens_TwoLimitsTwoPens()
    {
        var (_, session) = Draw(new TestPdf(), "3 M 0 0 m 10 10 l 20 0 l S 0.5 M 0 20 m 10 30 l 20 20 l S");
        var pens = session.All<DrawnGeometry>().Select(d => d.Pen).ToArray();
        Assert.That(pens[0].MiterLimit, Is.EqualTo(3));
        Assert.That(pens[1].MiterLimit, Is.EqualTo(1), "below 1 a miter limit is 1");
    }

    [Test]
    public void AZeroWidthLineIsTheThinnestTheScreenShows()
    {
        var (_, session) = Draw(new TestPdf(), "0 w 0 0 m 10 0 l S");
        Assert.That(session.All<DrawnGeometry>().Single().Pen.MinimumDeviceThickness, Is.EqualTo(1));
    }

    [Test]
    public void CmykAndGrayBecomeSrgb()
    {
        var (_, session) = Draw(new TestPdf(), "0.5 g 0 0 1 1 re f 0 0 0 1 k 0 0 1 1 re f");
        var colors = session.All<DrawnGeometry>().Select(d => ((SolidColorBrush)d.Brush).Color).ToArray();
        Assert.That(colors[0], Is.EqualTo(new Color(128, 128, 128, 255)));
        Assert.That(colors[1].R, Is.LessThan(64), "black ink is dark");
    }

    // A map's border is thousands of straight pieces: a run of them is one polyline, its points in order, and a curve
    // between two runs stays a curve.
    [Test]
    public void ARunOfStraightPieces_IsOnePolyline_InOrder()
    {
        // The lone piece in another color, so it stays a path of its own.
        var (_, session) = Draw(new TestPdf(),
            "0 0 m 10 0 l 10 10 l 20 10 l 30 0 40 0 50 10 c 60 10 l S 1 0 0 RG 0 50 m 5 50 l S");
        var paths = session.All<DrawnGeometry>().Select(d => (StreamGeometry)d.Geometry).ToArray();
        var figure = paths[0].Figures.Single();
        var start = figure.StartPoint;
        var segments = figure.Segments.ToArray();
        var lone = paths[1].Figures.Single().Segments.Single();
        paths[0].RecalculateBounds();

        Assert.Multiple(() =>
        {
            Assert.That(segments.Select(segment => segment.GetType()),
                Is.EqualTo(new[] { typeof(PolylineSegment), typeof(CubicBezierSegment), typeof(LineSegment) }));
            Assert.That(FromStart(start, segments[0]),
                Is.EqualTo(new[] { new Vector2<double>(10, 0), new Vector2<double>(10, 10), new Vector2<double>(20, 10) }));
            Assert.That(((CubicBezierSegment)segments[1]).Point - start, Is.EqualTo(new Vector2<double>(50, 10)));
            Assert.That(((LineSegment)segments[2]).Point - start, Is.EqualTo(new Vector2<double>(60, 10)));
            Assert.That(lone, Is.InstanceOf<LineSegment>(), "a lone piece is a line");
            Assert.That(paths[0].Bounds, Is.EqualTo(new Rect(start.X, start.Y, 60, 10)), "bounded by all its points");
        });
    }

    // Paths drawn one after another with the same opaque paint, in one part of the page, are one draw - as a map's tile
    // draws its features of one style - each still where it stood.
    [Test]
    public void PathsOfOnePaintInARow_AreOneDraw_EachWhereItStood()
    {
        var (_, session) = Draw(new TestPdf(), "0 0 10 10 re f 20 0 m 30 0 l 30 10 l f 40 0 m 50 0 l 50 10 l f");
        var drawn = session.All<DrawnGeometry>().ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(drawn, Has.Length.EqualTo(2), "the rectangle, a path apart, and the two paths after it as one");
            var joined = (StreamGeometry)drawn[1].Geometry;
            Assert.That(joined.Figures, Has.Count.EqualTo(2));
            Assert.That(At(drawn[1].Transform, joined.Figures[0].StartPoint.X, joined.Figures[0].StartPoint.Y),
                Is.EqualTo((20.0, 100.0)));
            Assert.That(At(drawn[1].Transform, joined.Figures[1].StartPoint.X, joined.Figures[1].StartPoint.Y),
                Is.EqualTo((40.0, 100.0)));
        });
    }

    // What joining would change is not joined: paint that is seen through, another paint, the even-odd rule, a dash -
    // or a part of the page far from the last.
    [TestCase("0.5 0 0 rg /Half gs 0 0 m 10 0 l 10 10 l f 20 0 m 30 0 l 30 10 l f", 2)]
    [TestCase("0 0 m 10 0 l 10 10 l f 1 0 0 rg 20 0 m 30 0 l 30 10 l f", 2)]
    [TestCase("0 0 m 10 0 l 10 10 l f* 20 0 m 30 0 l 30 10 l f*", 2)]
    [TestCase("[2 1] 0 d 0 0 m 10 0 l S 20 0 m 30 0 l S", 2)]
    [TestCase("0 0 m 10 0 l S 20 0 m 30 0 l S", 1)]
    // Two squares overlapping, turning apart: joined, the overlap's windings would cancel and leave a hole.
    [TestCase("0 0 m 10 0 l 10 10 l 0 10 l f 5 5 m 5 15 l 15 15 l 15 5 l f", 2)]
    [TestCase("0 0 m 10 0 l 10 10 l 0 10 l f 5 5 m 15 5 l 15 15 l 5 15 l f", 1)]
    public void OnlyWhatLooksTheSameJoined_IsJoined(string content, int draws)
    {
        var pdf = new TestPdf { Resources = "<< /ExtGState << /Half << /ca 0.5 >> >> >>" };
        var (_, session) = Draw(pdf, content);
        Assert.That(session.All<DrawnGeometry>().Count(), Is.EqualTo(draws));
    }

    // A path that is filled and then cuts what follows (W f) cuts by where it stands, though its fill is placed on the
    // grid: under a translation off the grid the cut was moved by what the fill's points were moved.
    [Test]
    public void APathFilledAndCuttingBy_CutsWhereItStands()
    {
        var (_, session) = Draw(new TestPdf(), "q 1 0 0 1 30.5 10.25 cm 0 0 m 50 0 l 50 50 l h W f 0 0 1 1 re f Q");
        var fill = session.All<DrawnGeometry>().First();
        var cut = session.All<OpenedGroup>().Single();
        var corner = ((StreamGeometry)cut.Options.Clip).Figures[0].StartPoint;

        Assert.Multiple(() =>
        {
            Assert.That(At(fill, 0, 0), Is.EqualTo((30.5, 89.75)));
            Assert.That(At(cut.Options.ClipTransform, corner.X, corner.Y), Is.EqualTo((30.5, 89.75)));
        });
    }

    // A page seen in part draws what meets the view and leaves out the rest - a group wholly outside with all it holds,
    // every group it begins ended. Before it is prepared it draws all of it.
    [Test]
    public void APageSeenInPart_DrawsWhatMeetsTheView()
    {
        var document = new TestPdf().Open(
            "1 0 0 rg 10 10 10 10 re f 0 0 1 rg 150 50 10 10 re f q 140 40 30 30 re W n 0 1 0 rg 145 45 5 5 re f Q");
        var page = PdfPageDrawing.Build(document, document.Pages[0]);
        var unprepared = new RecordingSession();
        page.Seen(new Rect(0, 60, 40, 40)).Render(unprepared, Matrix4x4<float>.Identity, null);

        page.PrepareForDrawing();
        var left = new RecordingSession();
        page.Seen(new Rect(0, 60, 40, 40)).Render(left, Matrix4x4<float>.Identity, null);
        var right = new RecordingSession();
        page.Seen(new Rect(130, 20, 50, 50)).Render(right, Matrix4x4<float>.Identity, null);

        Assert.Multiple(() =>
        {
            Assert.That(unprepared.All<DrawnGeometry>().Count(), Is.EqualTo(3), "not prepared, all of it");
            Assert.That(left.All<DrawnGeometry>().Count(), Is.EqualTo(1), "the red square alone, top left");
            Assert.That(left.All<OpenedGroup>().Count(), Is.EqualTo(1), "only the page's own cut: the clip is far off");
            Assert.That(left.Depth, Is.Zero);
            Assert.That(right.All<DrawnGeometry>().Count(), Is.EqualTo(2), "the blue square and the green one in its cut");
            Assert.That(right.Depth, Is.Zero, "every group begun is ended");
        });
    }

    // Placed by one matrix, as most pages place every path, paths far apart are still drawn apart: a view leaves out
    // the one it does not meet.
    [Test]
    public void PathsFarApartOnThePage_AreNotJoined()
    {
        var (_, session) = Draw(new TestPdf { MediaBox = "[0 0 1000 1000]" },
            "0 0 m 10 0 l 10 10 l f 600 600 m 610 600 l 610 610 l f");
        Assert.That(session.All<DrawnGeometry>().Count(), Is.EqualTo(2), "each in its own part of the page");
    }

    // A filled run that ends where it is closed keeps its last point, and the figure is closed.
    [Test]
    public void AClosedRun_KeepsItsLastPoint_AndIsClosed()
    {
        var (_, session) = Draw(new TestPdf(), "0 0 m 10 0 l 10 10 l 0 10 l h 20 20 m 30 20 l 30 30 l h f");
        var figures = ((StreamGeometry)session.All<DrawnGeometry>().Single().Geometry).Figures;
        var start = figures[0].StartPoint;

        Assert.Multiple(() =>
        {
            Assert.That(figures.Select(figure => figure.IsClosed), Is.EqualTo(new[] { true, true }));
            Assert.That(FromStart(start, figures[0].Segments.Single()),
                Is.EqualTo(new[] { new Vector2<double>(10, 0), new Vector2<double>(10, 10), new Vector2<double>(0, 10) }));
            Assert.That(FromStart(start, figures[1].Segments.Single()),
                Is.EqualTo(new[] { new Vector2<double>(30, 20), new Vector2<double>(30, 30) }));
        });
    }

    [Test]
    public void EvenOddFillsByItsRule()
    {
        var (_, session) = Draw(new TestPdf(), "0 0 m 10 0 l 10 10 l h 2 2 m 4 2 l 4 4 l h f*");
        var geometry = (StreamGeometry)session.All<DrawnGeometry>().Single().Geometry;
        Assert.That(geometry.FillRule, Is.EqualTo(FillRule.EvenOdd));
    }

    [Test]
    public void AClipCutsWhatFollowsUntilTheStateIsRestored()
    {
        var (_, session) = Draw(new TestPdf(),
            "q 0 0 m 10 0 l 5 10 l h W n 0 0 50 50 re f Q 0 0 50 50 re f");
        var calls = session.Calls;
        var group = (OpenedGroup)calls[0];
        Assert.That(group.Options.Clip, Is.InstanceOf<StreamGeometry>());
        Assert.That(((DrawnGeometry)calls[1]).Depth, Is.EqualTo(1));
        Assert.That(calls[2], Is.SameAs(ClosedGroup.Instance));
        Assert.That(((DrawnGeometry)calls[3]).Depth, Is.Zero);
    }

    [Test]
    public void PreparedForDrawing_ItsPathsClipsAndSoftMasks_AreAllProcessed()
    {
        var pdf = new TestPdf
        {
            Resources = "<< /ExtGState << /S << /SMask << /S /Luminosity /G 5 0 R >> >> >> >>",
        };
        pdf.AddStream("/Type /XObject /Subtype /Form /BBox [0 0 50 50] /Group << /S /Transparency /CS /DeviceGray >>",
            "1 g 0 0 m 50 0 l 25 50 l h f");
        var (page, session) = Draw(pdf, "q 0 0 m 10 0 l 5 10 l h W n /S gs 0 0 m 9 0 l 9 9 l h f Q 1 1 m 9 1 l S");

        page.PrepareForDrawing();

        var groups = session.All<OpenedGroup>().ToArray();
        var mask = new RecordingSession();
        groups.Single(group => group.Options.Mask != null).Options.Mask.Render(mask, Matrix4x4<float>.Identity, null);
        var geometries = session.All<DrawnGeometry>().Select(drawn => drawn.Geometry)
            .Concat(groups.Select(group => group.Options.Clip).Where(clip => clip != null))
            .Concat(mask.All<DrawnGeometry>().Select(drawn => drawn.Geometry))
            .ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(geometries, Has.Length.EqualTo(4), "two paths, a clip and the mask's path");
            Assert.That(geometries.Where(geometry => !geometry.IsProcessed), Is.Empty);
        });
    }

    [Test]
    public void PreparingCancelled_StopsQuietly_AndLeavesThePathsToTheirDraw()
    {
        var (page, session) = Draw(new TestPdf(), "0 0 m 10 0 l 5 10 l h f");
        using var cancelled = new System.Threading.CancellationTokenSource();
        cancelled.Cancel();

        Assert.DoesNotThrow(() => page.PrepareForDrawing(cancelled.Token));
        Assert.That(session.All<DrawnGeometry>().Single().Geometry.IsProcessed, Is.False);
    }

    [Test]
    public void AClipToOneRectangleIsARectangle_SoItCanBeAScissor()
    {
        var (_, session) = Draw(new TestPdf(), "10 10 30 20 re W n 0 0 50 50 re f");
        var clip = (RectangleGeometry)session.All<OpenedGroup>().Single().Options.Clip;
        Assert.That(clip.Rect, Is.EqualTo(new Rect(10, 10, 30, 20)));
    }

    [Test]
    public void APathPaintedAndClippedIsPaintedFirst_ThenCutsWhatFollows()
    {
        var (_, session) = Draw(new TestPdf(), "0 0 10 10 re W f 0 0 50 50 re f");
        Assert.That(session.Calls[0], Is.InstanceOf<DrawnGeometry>());
        Assert.That(session.Calls[1], Is.InstanceOf<OpenedGroup>());
        Assert.That(((DrawnGeometry)session.Calls[2]).Depth, Is.EqualTo(1));
    }

    [Test]
    public void ALoneMoveToPaintsNothing()
    {
        var (_, session) = Draw(new TestPdf(), "5 5 m f 5 5 m S");
        Assert.That(session.Calls, Is.Empty);
    }

    [Test]
    public void AnUnknownOperatorIsToldOnce_AndTheRestIsDrawn()
    {
        var (page, session) = Draw(new TestPdf(), "zz zz 0 0 1 1 re f");
        Assert.That(page.Problems, Has.Count.EqualTo(1));
        Assert.That(page.Problems[0], Does.Contain("zz"));
        Assert.That(session.All<DrawnGeometry>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void ARestoreWithNothingSavedIsIgnored()
    {
        var (_, session) = Draw(new TestPdf(), "Q Q 0 0 1 1 re f");
        Assert.That(session.All<DrawnGeometry>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void TheSameColorIsOneBrush()
    {
        var (_, session) = Draw(new TestPdf(), "1 0 0 rg 0 0 1 1 re f 2 2 1 1 re f");
        var brushes = session.All<DrawnGeometry>().Select(d => d.Brush).ToArray();
        Assert.That(brushes[0], Is.SameAs(brushes[1]));
        Assert.That(brushes[0].IsFrozen, Is.True);
    }
}
