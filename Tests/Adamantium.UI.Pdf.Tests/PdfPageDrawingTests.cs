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

    [Test]
    public void ThePageIsInItsPointsYDownFromTheTopLeft()
    {
        var (page, session) = Draw(new TestPdf(), "0 0 10 20 re f");
        Assert.That((page.Bounds.Width, page.Bounds.Height), Is.EqualTo((200.0, 100.0)));
        var drawn = session.All<DrawnGeometry>().Single();
        Assert.That(drawn.Geometry, Is.InstanceOf<RectangleGeometry>());
        Assert.That(At(drawn.Transform, 0, 0), Is.EqualTo((0.0, 100.0)));
        Assert.That(At(drawn.Transform, 10, 20), Is.EqualTo((10.0, 80.0)));
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
        var (ax, ay) = At(drawn.Transform, x, y);
        Assert.That(ax, Is.EqualTo(expectedX).Within(1e-4));
        Assert.That(ay, Is.EqualTo(expectedY).Within(1e-4));
    }

    [Test]
    public void TheCropBoxIsThePage()
    {
        var (page, session) = Draw(new TestPdf { PageExtra = "/CropBox [50 20 150 80]" }, "50 20 1 1 re f");
        Assert.That((page.Bounds.Width, page.Bounds.Height), Is.EqualTo((100.0, 60.0)));
        Assert.That(At(session.All<DrawnGeometry>().Single().Transform, 50, 20), Is.EqualTo((0.0, 60.0)));
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
