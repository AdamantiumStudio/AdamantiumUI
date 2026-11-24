using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Resources;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Drawings;
using Adamantium.UI.Core.Media.Imaging;
using Adamantium.UI.Core.Printing;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.UI.Pdf.Tests;

/// <summary>Documents saved as PDF and read back - by our reader, and drawn again by the page's own drawing: the sheet's
/// size, the pages, and what each draws in vector - gradients, pictures, faded and masked groups, meshes.</summary>
public class PdfPrintFileTests
{
    private static readonly Color Red = new(255, 0, 0, 255);
    private static readonly Color Blue = new(0, 0, 255, 255);

    private sealed class Pages(int count, Action<int, IDrawingSession> draw) : IPrintDocument
    {
        public string Title => "Pages";

        public PrintPage LaidOutOn { get; private set; }

        public int Paginate(PrintPage page)
        {
            LaidOutOn = page;
            return count;
        }

        public void DrawPage(int index, IDrawingSession session) => draw(index, session);
    }

    private static PdfDocument Save(IPrintDocument document, PrintPage page = null)
    {
        using var output = new MemoryStream();
        PdfPrintFile.Save(document, page ?? new PrintPage(PaperSize.A4), output);
        return PdfDocument.Open(output.ToArray());
    }

    private static PdfDocument Save(Action<IDrawingSession> draw) => Save(new Pages(1, (_, session) => draw(session)));

    /// <summary>The page drawn again by UI.Pdf, in its points, y down from the top left.</summary>
    private static RecordingSession Drawn(PdfDocument document, int index = 0)
    {
        var page = PdfPageDrawing.Build(document, document.Pages[index]);
        var session = new RecordingSession();
        page.Render(session, Matrix4x4<float>.Identity, null);
        Assert.That(session.Depth, Is.Zero, "every group is closed");
        session.UnwrapPage();
        return session;
    }

    private static string Content(PdfDocument document, int index = 0) =>
        Encoding.ASCII.GetString(document.Pages[index].GetContents().Span);

    private static PdfResources Resources(PdfDocument document, int index = 0) =>
        new(document.Pages[index].Resources, document);

    private static BitmapSource Picture(int width, int height, Color color)
    {
        var pixels = new byte[width * height * 4];
        for (var index = 0; index < width * height; index++)
        {
            pixels[index * 4] = color.R;
            pixels[index * 4 + 1] = color.G;
            pixels[index * 4 + 2] = color.B;
            pixels[index * 4 + 3] = color.A;
        }

        return new BitmapSource((uint)width, (uint)height, 1, 1, (SurfaceFormat)Format.R8G8B8A8_UNORM, pixels);
    }

    [Test]
    public void TheSheet_IsThePapersSize_InPoints_TurnedForLandscape()
    {
        var document = new Pages(3, (_, _) => { });
        var saved = Save(document, new PrintPage(PaperSize.A4, PageOrientation.Landscape));

        Assert.That(saved.PageCount, Is.EqualTo(3));
        Assert.That(saved.Pages[0].MediaBox.Width, Is.EqualTo(841.89).Within(0.01));
        Assert.That(saved.Pages[0].MediaBox.Height, Is.EqualTo(595.28).Within(0.01));
        Assert.That(document.LaidOutOn.Size.Width, Is.EqualTo(PaperSize.A4.Height), "laid out on the sheet as printed");
        Assert.That(saved.Properties.Title, Is.EqualTo("Pages"));
    }

    [Test]
    public void ARectangle_ComesBackWhereItWasDrawn_InTheSheetsPoints()
    {
        var saved = Save(session => session.DrawRectangle(new SolidColorBrush(Red), new Rect(96, 192, 48, 24)));

        var drawn = Drawn(saved).All<DrawnGeometry>().Single();
        drawn.Geometry.RecalculateBounds();
        var box = drawn.Geometry.Bounds;
        var (left, top) = PdfPageDrawingTests.At(drawn.Transform, box.X, box.Y);
        var (right, bottom) = PdfPageDrawingTests.At(drawn.Transform, box.Right, box.Bottom);
        Assert.Multiple(() =>
        {
            Assert.That(Math.Min(left, right), Is.EqualTo(72).Within(1e-3), "an inch in");
            Assert.That(Math.Min(top, bottom), Is.EqualTo(144).Within(1e-3), "two inches down");
            Assert.That(Math.Abs(right - left), Is.EqualTo(36).Within(1e-3));
            Assert.That(Math.Abs(bottom - top), Is.EqualTo(18).Within(1e-3));
        });
    }

    // Faded as one, as a layer is on screen: two halves overlapping are not darker where they meet.
    [Test]
    public void AFadedGroup_IsATransparencyGroup_FadedAsOne()
    {
        var saved = Save(session =>
        {
            session.PushGroup(new DrawingGroupOptions { Opacity = 0.5 });
            session.DrawRectangle(new SolidColorBrush(Red), new Rect(0, 0, 40, 40));
            session.DrawRectangle(new SolidColorBrush(Red), new Rect(20, 0, 40, 40));
            session.PopGroup();
        });

        var resources = Resources(saved);
        var form = resources.XObject("Fm0");
        Assert.That(((PdfName)((PdfDictionary)form.Dictionary["Group"])["S"]).Value, Is.EqualTo("Transparency"));
        Assert.That(((PdfReal)resources.ExtGState("GS0")["ca"]).Value, Is.EqualTo(0.5).Within(1e-9));
        var recorded = Drawn(saved);
        Assert.That(recorded.All<OpenedGroup>().Any(group => Math.Abs(group.Options.Opacity - 0.5) < 1e-9), Is.True);
        Assert.That(recorded.All<DrawnGeometry>().All(drawn => ((SolidColorBrush)drawn.Brush).Color.A == 255), Is.True,
            "the halves are drawn opaque inside it");
    }

    [Test]
    public void ABlendedGroup_IsLaidDownBlended()
    {
        var saved = Save(session =>
        {
            session.PushGroup(new DrawingGroupOptions { BlendMode = BlendMode.Multiply });
            session.DrawRectangle(new SolidColorBrush(Red), new Rect(0, 0, 10, 10));
            session.PopGroup();
        });

        Assert.That(((PdfName)Resources(saved).ExtGState("BM0")["BM"]).Value, Is.EqualTo("Multiply"));
        Assert.That(Content(saved), Does.Contain("/BM0 gs").And.Contain("/Fm0 Do"));
    }

    [Test]
    public void AMaskedGroup_IsLetThroughBySoftMask()
    {
        var mask = new GeometryDrawing
        {
            Brush = new SolidColorBrush(Colors.White),
            Geometry = new RectangleGeometry(new Rect(0, 0, 20, 20)),
        };
        var saved = Save(session =>
        {
            session.PushGroup(new DrawingGroupOptions { Mask = mask, MaskKind = MaskKind.Luminosity });
            session.DrawRectangle(new SolidColorBrush(Red), new Rect(0, 0, 40, 40));
            session.PopGroup();
        });

        var state = Resources(saved).ExtGState("SM0");
        var softMask = (PdfDictionary)saved.Resolve(state["SMask"]);
        Assert.That(((PdfName)softMask["S"]).Value, Is.EqualTo("Luminosity"));
        Assert.That(saved.Resolve(softMask["G"]), Is.TypeOf<PdfStream>());
        Assert.That(Drawn(saved).All<OpenedGroup>().Any(group => group.Options.Mask != null), Is.True);
    }

    [Test]
    public void ALinearGradient_IsAnAxialShading_OverTheShapesBounds()
    {
        var gradient = new LinearGradientBrush(
            [new GradientStop(Red, 0), new GradientStop(Blue, 1)])
        {
            StartPoint = new Vector2<double>(0, 0),
            EndPoint = new Vector2<double>(1, 0),
        };
        var saved = Save(session => session.DrawRectangle(gradient, new Rect(0, 0, 96, 48)));

        var shading = (PdfDictionary)Resources(saved).Shading("Sh0");
        Assert.That(((PdfInteger)shading["ShadingType"]).Value, Is.EqualTo(2));
        Assert.That(Content(saved), Does.Contain("W n").And.Contain("/Sh0 sh"));
        Assert.That(Resources(saved).Dictionary.ContainsKey("ExtGState"), Is.False, "an opaque gradient needs no mask");
    }

    [Test]
    public void AGradientsAlpha_IsASoftMaskOfTheSameShape()
    {
        var gradient = new RadialGradientBrush(
            [new GradientStop(Red, 0), new GradientStop(new Color(255, 0, 0, 0), 1)]);
        var saved = Save(session => session.DrawEllipse(new Rect(0, 0, 50, 50), gradient, null, null));

        var state = Resources(saved).ExtGState("SM0");
        var mask = (PdfStream)saved.Resolve(((PdfDictionary)saved.Resolve(state["SMask"]))["G"]);
        var inner = new PdfResources((PdfDictionary)saved.Resolve(mask.Dictionary["Resources"]), saved);
        var alpha = (PdfDictionary)inner.Shading("Sh0");
        Assert.That(((PdfInteger)alpha["ShadingType"]).Value, Is.EqualTo(3), "the alpha runs as the colors do");
        Assert.That(((PdfName)alpha["ColorSpace"]).Value, Is.EqualTo("DeviceGray"));
    }

    [Test]
    public void AGradientStroke_IsAShadingPattern()
    {
        var gradient = new LinearGradientBrush([new GradientStop(Red, 0), new GradientStop(Blue, 1)]);
        var saved = Save(session => session.DrawRectangle(null, new Rect(10, 10, 50, 30), new Pen(gradient, 4)));

        Assert.That(Content(saved), Does.Contain("/Pattern CS /P0 SCN").And.Contain("\nS\n"));
        Assert.That(((PdfInteger)((PdfDictionary)Resources(saved).Pattern("P0"))["PatternType"]).Value, Is.EqualTo(2));
    }

    [Test]
    public void APicture_IsAnImage_WrittenOnceForEveryPageItIsOn()
    {
        var picture = Picture(4, 2, Blue);
        var saved = Save(new Pages(3, (_, session) =>
            session.DrawImage(picture, null, new Rect(0, 0, 40, 20), new CornerRadius(0))));

        var images = Enumerable.Range(0, 3)
            .Select(page => ((PdfDictionary)saved.Pages[page].Resources["XObject"])["Im0"])
            .Cast<PdfReference>().Select(reference => reference.Id).Distinct().ToList();
        Assert.That(images, Has.Count.EqualTo(1));
        var drawn = Drawn(saved).All<DrawnImage>().Single();
        var (left, top) = PdfPageDrawingTests.At(drawn.Transform, 0, 0);
        var (right, bottom) = PdfPageDrawingTests.At(drawn.Transform, 1, 1);
        Assert.That((Math.Abs(right - left), Math.Abs(bottom - top)), Is.EqualTo((30.0, 15.0)).Within(1e-3));
    }

    [Test]
    public void APartOfAPicture_IsCutToItsRect()
    {
        var saved = Save(session => session.DrawImage(Picture(10, 10, Red), null, new Rect(0, 0, 20, 20),
            new CornerRadius(4), new Rect(0.5, 0, 0.5, 1)));

        Assert.That(Content(saved), Does.Contain("W n").And.Contain("/Im0 Do"));
        var drawn = Drawn(saved).All<DrawnImage>().Single();
        var (left, _) = PdfPageDrawingTests.At(drawn.Transform, 0, 0);
        var (right, _) = PdfPageDrawingTests.At(drawn.Transform, 1, 1);
        Assert.That(Math.Abs(right - left), Is.EqualTo(30).Within(1e-3), "the whole picture is twice the rect wide");
    }

    [Test]
    public void ADrawingsPicture_StaysVector()
    {
        var drawing = new GeometryDrawing
        {
            Brush = new SolidColorBrush(Red),
            Geometry = new RectangleGeometry(new Rect(0, 0, 10, 10)),
        };
        var saved = Save(session => session.DrawImage(new DrawingImage { Drawing = drawing }, null,
            new Rect(0, 0, 40, 40), new CornerRadius(0)));

        Assert.That(Resources(saved).Dictionary.ContainsKey("XObject"), Is.False, "no image");
        Assert.That(Drawn(saved).All<DrawnGeometry>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void AColoredMesh_IsShadedTriangles()
    {
        var mesh = new ColoredMesh(
        [
            new ColoredVertex(new Vector2<double>(0, 0), Red),
            new ColoredVertex(new Vector2<double>(40, 0), Blue),
            new ColoredVertex(new Vector2<double>(0, 40), Red),
        ]);
        var saved = Save(session => session.DrawColoredMesh(mesh, Matrix4x4<float>.Identity));

        var shading = (PdfStream)Resources(saved).Shading("Sh0");
        Assert.That(((PdfInteger)shading.Dictionary["ShadingType"]).Value, Is.EqualTo(4));
        Assert.That(Drawn(saved).All<DrawnMesh>().Single().Mesh.Vertices.Count, Is.GreaterThanOrEqualTo(3),
            "read back as triangles - the reader may cut them finer");
    }

    // Four widths: the ring between the rect and the rect less each side's width, filled by the even-odd rule.
    [Test]
    public void ABorder_IsTheRingOfItsFourWidths()
    {
        var saved = Save(session => session.DrawBorder(null, new Rect(0, 0, 100, 50), new CornerRadius(0),
            new SolidColorBrush(Blue), new Thickness(1, 2, 3, 4)));

        var drawn = Drawn(saved).All<DrawnGeometry>().Single();
        Assert.That(Content(saved), Does.Contain("f*"));
        drawn.Geometry.RecalculateBounds();
        var box = drawn.Geometry.Bounds;
        var (left, _) = PdfPageDrawingTests.At(drawn.Transform, box.X, box.Y);
        var (right, _) = PdfPageDrawingTests.At(drawn.Transform, box.Right, box.Bottom);
        Assert.That(Math.Abs(right - left), Is.EqualTo(75).Within(1e-3), "the outer edge, in points");
    }

    [Test]
    public void AGroupLeftOpen_IsClosedWithItsPage()
    {
        var saved = Save(session =>
        {
            session.PushGroup(new DrawingGroupOptions { Opacity = 0.5 });
            session.DrawRectangle(new SolidColorBrush(Red), new Rect(0, 0, 10, 10));
        });

        Assert.That(Content(saved), Does.Contain("/Fm0 Do"));
    }

    // A square end on the diagonal reaches half the width times the root of two past the line; a faded group's box -
    // which cuts it - holds it.
    [Test]
    public void ASquareEnd_IsInsideItsGroupsBox()
    {
        var content = new Adamantium.Pdf.Writing.PdfContentBuilder();
        var session = new PdfContentSession(content, Adamantium.Pdf.Document.PdfMatrix.Identity);
        session.PushGroup(new DrawingGroupOptions { Opacity = 0.5 });
        session.DrawLine(new Vector2<double>(0, 0), new Vector2<double>(100, 100),
            new Pen(new SolidColorBrush(Red), 20, startLineCap: PenLineCap.Square, endLineCap: PenLineCap.Square,
                penLineJoin: PenLineJoin.Round));
        session.PopGroup();

        var form = (PdfStream)((PdfDictionary)content.ToResources()["XObject"])["Fm0"];
        var box = ((PdfArray)form.Dictionary["BBox"]).Cast<PdfReal>().Select(real => real.Value).ToArray();
        Assert.That(box[2], Is.GreaterThanOrEqualTo(100 + 10 * Math.Sqrt(2) - 1e-6));
        Assert.That(box[0], Is.LessThanOrEqualTo(-10 * Math.Sqrt(2) + 1e-6));
    }

    // Repeated, a gradient whose stops do not reach its ends keeps its end colors to them, and the next repeat starts
    // at a hard edge - as on screen - not a blend from blue back into red.
    [Test]
    public void ARepeatedGradient_MeetsItsNextRepeatAtAHardEdge()
    {
        var gradient = new LinearGradientBrush([new GradientStop(Red, 0.2), new GradientStop(Blue, 0.8)])
        {
            StartPoint = new Vector2<double>(0, 0),
            EndPoint = new Vector2<double>(0.5, 0),
            SpreadMethod = GradientSpreadMethod.Repeat,
        };
        var saved = Save(session => session.DrawRectangle(gradient, new Rect(0, 0, 100, 50)));

        var function = (PdfDictionary)((PdfDictionary)Resources(saved).Shading("Sh0"))["Function"];
        var bounds = ((PdfArray)function["Bounds"]).Cast<PdfReal>().Select(real => real.Value).ToList();
        Assert.That(bounds.Count(bound => Math.Abs(bound - 0.5) < 1e-9), Is.EqualTo(2), "blue up to the seam, red after");
    }

    // A gradient laid along the line has no PDF paint; it is not written laid across the shape instead.
    [Test]
    public void AGradientAlongTheLine_IsNotWrittenAcrossTheShape()
    {
        var gradient = new LinearGradientBrush([new GradientStop(Red, 0), new GradientStop(Blue, 1)]);
        var saved = Save(session => session.DrawRectangle(null, new Rect(10, 10, 50, 30),
            new Pen(gradient, 4) { BrushMapping = PenBrushMapping.Along }));

        Assert.That(Resources(saved).Dictionary.ContainsKey("Pattern"), Is.False);
    }

    [Test]
    public void AClipOfNothing_LetsNothingThrough()
    {
        var saved = Save(session =>
        {
            session.PushGroup(new DrawingGroupOptions { Clip = new StreamGeometry() });
            session.DrawRectangle(new SolidColorBrush(Red), new Rect(0, 0, 40, 40));
            session.PopGroup();
        });

        Assert.That(Content(saved), Does.Match(@"0 0 0 0 re\nW\*? n"));
    }

    [Test]
    public void APictureSampledTwoWays_IsTwoImages()
    {
        var smooth = Picture(2, 2, Red);
        var blocky = new BitmapSource(2, 2, 1, 1, smooth.SurfaceLayout, smooth.PixelBytes)
        {
            Sampling = ImageSampling.Pixelated,
        };
        var saved = Save(session =>
        {
            session.DrawImage(smooth, null, new Rect(0, 0, 10, 10), new CornerRadius(0));
            session.DrawImage(blocky, null, new Rect(20, 0, 10, 10), new CornerRadius(0));
        });

        var resources = Resources(saved);
        Assert.That(resources.XObject("Im0").Dictionary.ContainsKey("Interpolate"), Is.True);
        Assert.That(resources.XObject("Im1").Dictionary.ContainsKey("Interpolate"), Is.False);
    }

    [Test]
    public void ADocumentOfNoPages_IsOneBlankSheet()
    {
        var saved = Save(new Pages(0, (_, _) => { }));

        Assert.That(saved.PageCount, Is.EqualTo(1));
        Assert.That(Content(saved), Is.Empty);
    }

    // A mask's picture cannot end the group it masks: an extra pop inside it is refused, not a crash halfway.
    [Test]
    public void AMaskThatEndsAGroupTooMany_IsRefused()
    {
        var session = new PdfContentSession(new Adamantium.Pdf.Writing.PdfContentBuilder(),
            Adamantium.Pdf.Document.PdfMatrix.Identity);
        session.PushGroup(new DrawingGroupOptions { Mask = new PoppingDrawing() });
        session.DrawRectangle(new SolidColorBrush(Red), new Rect(0, 0, 10, 10));

        Assert.Throws<InvalidOperationException>(() => session.PopGroup());
    }

    // A group faded as one goes into the content as it ends: closed, an open one is written.
    [Test]
    public void AnOpenGroup_ClosedByHand_IsWrittenAndBounded()
    {
        var content = new Adamantium.Pdf.Writing.PdfContentBuilder();
        var session = new PdfContentSession(content, Adamantium.Pdf.Document.PdfMatrix.Identity);
        session.PushGroup(new DrawingGroupOptions { Opacity = 0.5 });
        session.DrawRectangle(new SolidColorBrush(Red), new Rect(0, 0, 10, 10));
        session.CloseGroups();

        Assert.That(Encoding.ASCII.GetString(content.ToContent()), Does.Contain("/Fm0 Do"));
        Assert.That(session.Bounds.IsEmpty, Is.False);
    }

    private sealed class PoppingDrawing : Drawing
    {
        public override Rect Bounds => new(0, 0, 10, 10);

        public override void Render(IDrawingSession session, Matrix4x4<float> transform, Brush currentColor) =>
            session.PopGroup();
    }

    [Test]
    public void CalledOff_ItStopsBetweenPages()
    {
        using var stop = new CancellationTokenSource();
        var document = new Pages(5, (index, _) =>
        {
            if (index == 1)
            {
                stop.Cancel();
            }
        });

        Assert.Throws<OperationCanceledException>(() =>
            PdfPrintFile.Save(document, new PrintPage(PaperSize.Letter), new MemoryStream(), stop.Token));
    }
}
