using System;
using System.Linq;
using System.Text;
using Adamantium.Mathematics;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Writing;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using NUnit.Framework;

namespace Adamantium.UI.Pdf.Tests;

/// <summary>What is drawn on a sheet, written into a page's content and read back by the page's own drawing: it comes
/// back where it was drawn on the sheet, turned or cut as the page is, in its colors and widths.</summary>
public class PdfContentSessionTests
{
    private static readonly Color Red = new(255, 0, 0, 255);
    private static readonly Color Blue = new(0, 0, 255, 255);

    private static RecordingSession RoundTrip(string pageExtra, Action<PdfContentSession> draw,
        out PdfContentSession session)
    {
        var page = new TestPdf { PageExtra = pageExtra }.Open("").Pages[0];
        var content = new PdfContentBuilder();
        session = new PdfContentSession(content, PdfPageSpace.ToSheet(page).Inverse()!.Value);
        draw(session);
        var (_, recorded) = PdfPageDrawingTests.Draw(new TestPdf { PageExtra = pageExtra },
            Encoding.ASCII.GetString(content.ToContent()));
        return recorded;
    }

    private static Rect OnSheet(DrawnGeometry drawn)
    {
        var box = drawn.Geometry.Bounds;
        var corners = new[]
        {
            PdfPageDrawingTests.At(drawn.Transform, box.X, box.Y),
            PdfPageDrawingTests.At(drawn.Transform, box.Right, box.Y),
            PdfPageDrawingTests.At(drawn.Transform, box.X, box.Bottom),
            PdfPageDrawingTests.At(drawn.Transform, box.Right, box.Bottom),
        };
        var left = corners.Min(corner => corner.X);
        var top = corners.Min(corner => corner.Y);
        return new Rect(left, top, corners.Max(corner => corner.X) - left, corners.Max(corner => corner.Y) - top);
    }

    private static void IsNear(Rect actual, Rect expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(1e-3), "left");
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(1e-3), "top");
            Assert.That(actual.Width, Is.EqualTo(expected.Width).Within(1e-3), "width");
            Assert.That(actual.Height, Is.EqualTo(expected.Height).Within(1e-3), "height");
        });
    }

    [TestCase("")]
    [TestCase("/Rotate 90")]
    [TestCase("/Rotate 270")]
    [TestCase("/CropBox [50 20 150 80]")]
    [TestCase("/CropBox [50 20 150 80] /Rotate 180")]
    public void ARectangleComesBackWhereItWasDrawnOnTheSheet(string page)
    {
        var recorded = RoundTrip(page,
            session => session.DrawRectangle(new SolidColorBrush(Red), new Rect(10, 20, 30, 15)), out _);

        IsNear(OnSheet(recorded.All<DrawnGeometry>().Single()), new Rect(10, 20, 30, 15));
    }

    [Test]
    public void AFillAndAStrokeKeepTheirColorsWidthAndJoin()
    {
        var pen = new Pen(new SolidColorBrush(Blue), 4, penLineJoin: PenLineJoin.Round);
        var recorded = RoundTrip("", session =>
            session.DrawEllipse(new Rect(10, 10, 40, 20), new SolidColorBrush(Red), pen, null), out _);

        var drawn = recorded.All<DrawnGeometry>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(((SolidColorBrush)drawn.Brush).Color, Is.EqualTo(Red));
            Assert.That(((SolidColorBrush)drawn.Pen.Brush).Color, Is.EqualTo(Blue));
            Assert.That(drawn.Pen.Thickness, Is.EqualTo(4).Within(1e-6));
            Assert.That(drawn.Pen.PenLineJoin, Is.EqualTo(PenLineJoin.Round));
        });
        IsNear(OnSheet(drawn), new Rect(10, 10, 40, 20));
    }

    // A placed drawing is where its placement puts it: the canvas states an item's drawing from its own corner.
    [Test]
    public void APlacementMovesWhatIsDrawn()
    {
        var placement = new Placement { Matrix = Matrix4x4.CreateTranslation<float>(60, 30, 0) };
        var recorded = RoundTrip("/Rotate 90", session =>
            session.DrawRectangle(new SolidColorBrush(Red), new Rect(0, 0, 10, 5), new CornerRadius(0), null,
                placement), out _);

        IsNear(OnSheet(recorded.All<DrawnGeometry>().Single()), new Rect(60, 30, 10, 5));
    }

    [Test]
    public void InkIsARoundStrokeThroughItsPoints()
    {
        var ink = new InkBrush
        {
            Points = [new Vector2<float>(10, 10), new Vector2<float>(50, 10), new Vector2<float>(50, 40)],
            Count = 3,
            Thickness = 6,
            Color = Blue,
        };
        var recorded = RoundTrip("", session => session.DrawRectangle(ink, new Rect(10, 10, 40, 30)), out var written);

        var drawn = recorded.All<DrawnGeometry>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(drawn.Brush, Is.Null, "ink is a stroke, not a fill");
            Assert.That(((SolidColorBrush)drawn.Pen.Brush).Color, Is.EqualTo(Blue));
            Assert.That(drawn.Pen.Thickness, Is.EqualTo(6).Within(1e-6));
            Assert.That(drawn.Pen.StartLineCap, Is.EqualTo(PenLineCap.ConvexRound));
            Assert.That(drawn.Pen.PenLineJoin, Is.EqualTo(PenLineJoin.Round));
        });
        IsNear(OnSheet(drawn), new Rect(10, 10, 40, 30));
        Assert.That(written.Bounds, Is.EqualTo(new PdfRectangle(7, 57, 53, 93)), "the page is y up, 100 high");
    }

    // Ink pressed harder is drawn wider, as one filled shape - round at every point - so a translucent stroke is laid
    // down once and shows no beads where its pieces meet.
    [Test]
    public void InkOfChangingWidthIsOneFilledShape()
    {
        var ink = new InkBrush
        {
            Points = [new Vector2<float>(10, 10), new Vector2<float>(50, 10), new Vector2<float>(90, 10)],
            Widths = [1f, 1f, 3f],
            Count = 3,
            Thickness = 2,
            Color = Blue,
        };
        var recorded = RoundTrip("", session => session.DrawRectangle(ink, new Rect(10, 10, 80, 0)), out _);

        var drawn = recorded.All<DrawnGeometry>().Single();
        Assert.That(drawn.Pen, Is.Null);
        Assert.That(drawn.Brush, Is.Not.Null);
        IsNear(OnSheet(drawn), new Rect(9, 7, 84, 6));
    }

    // As the arrow pass draws it: a shaft with round ends and the head in one shape, the shaft stopping at the head's
    // base.
    [Test]
    public void AnArrowIsOneFilledShape_ItsShaftRoundEnded()
    {
        var arrow = new CanvasArrowBrush
        {
            From = new Vector2<double>(10, 50),
            To = new Vector2<double>(110, 50),
            Thickness = 4,
            EndHead = 2,
            HeadLength = 20,
            HeadWidth = 10,
            Color = Red,
        };
        var recorded = RoundTrip("", session => session.DrawRectangle(arrow, new Rect(10, 40, 100, 20)), out _);

        var drawn = recorded.All<DrawnGeometry>().Single();
        Assert.That(drawn.Brush, Is.Not.Null);
        IsNear(OnSheet(drawn), new Rect(8, 40, 102, 20));
    }

    // A head never longer than the arrow: on a short one it would start behind the tail and point the wrong way.
    [Test]
    public void AHeadIsNeverLongerThanItsArrow()
    {
        var arrow = new CanvasArrowBrush
        {
            From = new Vector2<double>(10, 50),
            To = new Vector2<double>(15, 50),
            Thickness = 2,
            EndHead = 2,
            HeadLength = 12,
            HeadWidth = 4,
            Color = Red,
        };
        var recorded = RoundTrip("", session => session.DrawRectangle(arrow, new Rect(10, 46, 5, 8)), out _);

        var drawn = OnSheet(recorded.All<DrawnGeometry>().Single());
        Assert.That(drawn.X, Is.EqualTo(9).Within(1e-3), "nothing behind the tail but the shaft's round end");
    }

    // A miter corner reaches past half the width: the box must hold it, or an appearance cut to it loses the tips.
    [Test]
    public void TheBoxHoldsAMiterCorner()
    {
        var session = new PdfContentSession(new PdfContentBuilder(), PdfMatrix.Identity);
        var turn = new Placement
        {
            Matrix = Matrix4x4.CreateRotationZ<float>(MathF.PI / 4) * Matrix4x4.CreateTranslation<float>(50, 50, 0)
        };
        session.DrawRectangle(null, new Rect(-10, -10, 20, 20), new CornerRadius(0),
            new Pen(new SolidColorBrush(Blue), 10, penLineJoin: PenLineJoin.Miter), turn);

        var tip = 10 * Math.Sqrt(2) + 5 * Math.Sqrt(2);
        Assert.That(session.Bounds.Left, Is.LessThanOrEqualTo(50 - tip + 1e-6));
        Assert.That(session.Bounds.Right, Is.GreaterThanOrEqualTo(50 + tip - 1e-6));
    }

    [Test]
    public void AGroupsClipCutsWhatIsDrawnInIt()
    {
        var recorded = RoundTrip("", session =>
        {
            session.PushGroup(new DrawingGroupOptions { Clip = new RectangleGeometry(new Rect(0, 0, 20, 20)) });
            session.DrawRectangle(new SolidColorBrush(Red), new Rect(10, 10, 30, 30));
            session.PopGroup();
        }, out _);

        var group = recorded.All<OpenedGroup>().Single();
        Assert.That(group.Options.Clip, Is.Not.Null, "the clip came back as a group cut by it");
        Assert.That(recorded.All<DrawnGeometry>().Single().Depth, Is.EqualTo(group.Depth + 1));
    }

    // Opacity is a graphics state the form names: a group's and the brush's are one alpha.
    [Test]
    public void AGroupsOpacityAndTheBrushsAreOneAlpha()
    {
        var content = new PdfContentBuilder();
        var session = new PdfContentSession(content, PdfMatrix.Identity);
        session.PushGroup(new DrawingGroupOptions { Opacity = 0.5 });
        session.DrawRectangle(new SolidColorBrush(Red, 0.5), new Rect(0, 0, 10, 10));
        session.PopGroup();

        var form = content.ToFormXObject(new PdfRectangle(0, 0, 10, 10));
        var resources = form.Dictionary["Resources"] as PdfDictionary;
        var state = (resources?["ExtGState"] as PdfDictionary)?["GS0"] as PdfDictionary;
        Assert.That((state?["ca"] as PdfReal)?.Value, Is.EqualTo(0.25).Within(1e-9));
    }

    [Test]
    public void APopWithNoGroupIsAnError()
    {
        var session = new PdfContentSession(new PdfContentBuilder(), PdfMatrix.Identity);
        Assert.Throws<InvalidOperationException>(() => session.PopGroup());
    }

    // A picture has no paths to give: it is left out, and nothing is written for it.
    [Test]
    public void APictureIsLeftOut()
    {
        var content = new PdfContentBuilder();
        var session = new PdfContentSession(content, PdfMatrix.Identity);
        session.DrawImage(null, null, new Rect(0, 0, 10, 10), new CornerRadius(0));
        Assert.That(content.ToContent(), Is.Empty);
        Assert.That(session.Bounds.IsEmpty, Is.True);
    }
}
