using System.Linq;
using System.Threading.Tasks;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Media;
using NUnit.Framework;
using static Adamantium.UI.Pdf.Tests.PdfPageDrawingTests;

namespace Adamantium.UI.Pdf.Tests;

/// <summary>Forms, groups, soft masks, optional content, images and annotations.</summary>
public class PdfObjectDrawingTests
{
    [Test]
    public void AFormIsPlacedByItsMatrixAndCutToItsBox()
    {
        var pdf = new TestPdf { Resources = "<< /XObject << /F 5 0 R >> >>" };
        pdf.AddStream("/Type /XObject /Subtype /Form /BBox [0 0 10 10] /Matrix [2 0 0 2 30 0]", "0 0 5 5 re f");
        var (_, session) = Draw(pdf, "/F Do");
        var clip = session.All<OpenedGroup>().Single();
        Assert.That(clip.Options.Clip, Is.InstanceOf<RectangleGeometry>());
        var drawn = session.All<DrawnGeometry>().Single();
        Assert.That(drawn.Depth, Is.EqualTo(1));
        Assert.That(At(drawn.Transform, 5, 5), Is.EqualTo((40.0, 90.0)));
    }

    [Test]
    public void AFormDrawingItselfIsNotEnteredAgain()
    {
        var pdf = new TestPdf { Resources = "<< /XObject << /F 5 0 R >> >>" };
        pdf.AddStream("/Type /XObject /Subtype /Form /BBox [0 0 10 10] /Resources << /XObject << /F 5 0 R >> >>",
            "0 0 1 1 re f /F Do");
        var (page, session) = Draw(pdf, "/F Do");
        Assert.That(session.All<DrawnGeometry>().Count(), Is.EqualTo(1));
        Assert.That(page.Problems.Any(p => p.Contains("draws itself")), Is.True);
    }

    [Test]
    public void AStateSavedInAFormAndNotRestoredEndsWithIt()
    {
        var pdf = new TestPdf { Resources = "<< /XObject << /F 5 0 R >> >>" };
        pdf.AddStream("/Type /XObject /Subtype /Form /BBox [0 0 10 10]", "q 1 0 0 rg 0 0 1 1 re W n");
        var (_, session) = Draw(pdf, "/F Do 0 0 1 1 re f");
        var last = session.All<DrawnGeometry>().Single();
        Assert.That(last.Depth, Is.Zero);
        Assert.That(((SolidColorBrush)last.Brush).Color, Is.EqualTo(new Color(0, 0, 0, 255)));
    }

    [Test]
    public void AFillOpacityIsTheBrushsOwn()
    {
        var pdf = new TestPdf { Resources = "<< /ExtGState << /G << /ca 0.5 /CA 0.25 >> >> >>" };
        var (_, session) = Draw(pdf, "/G gs 0 0 1 1 re B");
        var drawn = session.All<DrawnGeometry>().Single();
        Assert.That(((SolidColorBrush)drawn.Brush).Color.A, Is.EqualTo(128));
        Assert.That(((SolidColorBrush)drawn.Pen.Brush).Color.A, Is.EqualTo(64));
        Assert.That(session.All<OpenedGroup>(), Is.Empty);
    }

    [Test]
    public void ABlendModeWrapsEachObjectInAGroupOfItsOwn()
    {
        var pdf = new TestPdf { Resources = "<< /ExtGState << /M << /BM /Multiply >> >> >>" };
        var (_, session) = Draw(pdf, "/M gs 0 0 1 1 re f 2 2 1 1 re f");
        var groups = session.All<OpenedGroup>().ToArray();
        Assert.That(groups, Has.Length.EqualTo(2));
        Assert.That(groups.All(g => g.Options.BlendMode == BlendMode.Multiply), Is.True);
        Assert.That(session.All<DrawnGeometry>().All(d => d.Depth == 1), Is.True);
    }

    [Test]
    public void AFormWithATransparencyGroupIsOneObject_AndStartsOverInside()
    {
        var pdf = new TestPdf { Resources = "<< /XObject << /F 5 0 R >> /ExtGState << /G << /ca 0.5 /BM /Screen >> >> >>" };
        pdf.AddStream("/Type /XObject /Subtype /Form /BBox [0 0 10 10] /Group << /S /Transparency >>",
            "0 0 1 1 re f 2 2 1 1 re f");
        var (_, session) = Draw(pdf, "/G gs /F Do");
        var group = session.All<OpenedGroup>().First();
        Assert.That(group.Options.Opacity, Is.EqualTo(0.5));
        Assert.That(group.Options.BlendMode, Is.EqualTo(BlendMode.Screen));
        var inside = session.All<DrawnGeometry>().ToArray();
        Assert.That(inside, Has.Length.EqualTo(2));
        Assert.That(inside.All(d => ((SolidColorBrush)d.Brush).Color.A == 255), Is.True, "the alpha went to the group");
        Assert.That(session.All<OpenedGroup>().Count(g => g.Options.BlendMode == BlendMode.Screen), Is.EqualTo(1));
    }

    [Test]
    public void ASoftMaskIsDrawnIntoAPictureAndMasksWhatFollows()
    {
        var pdf = new TestPdf
        {
            Resources = "<< /ExtGState << /S << /SMask << /S /Luminosity /G 5 0 R /BC [0.5] >> >> >> >>",
        };
        pdf.AddStream("/Type /XObject /Subtype /Form /BBox [0 0 50 50] /Group << /S /Transparency /CS /DeviceGray >>",
            "1 g 0 0 50 50 re f");
        var (_, session) = Draw(pdf, "/S gs 0 0 10 10 re f");
        var group = session.All<OpenedGroup>().Single();
        Assert.That(group.Options.MaskKind, Is.EqualTo(MaskKind.Luminosity));
        Assert.That(group.Options.MaskBackdrop, Is.EqualTo(new Color(128, 128, 128, 255)));
        var mask = new RecordingSession();
        group.Options.Mask.Render(mask, group.Options.MaskTransform, null);
        var white = mask.All<DrawnGeometry>().Single();
        Assert.That(((SolidColorBrush)white.Brush).Color, Is.EqualTo(new Color(255, 255, 255, 255)));
        Assert.That(session.All<DrawnGeometry>().Single().Depth, Is.EqualTo(1));
    }

    [Test]
    public void ASoftMaskOfNoneTakesTheMaskAway()
    {
        var pdf = new TestPdf
        {
            Resources = "<< /ExtGState << /S << /SMask << /S /Alpha /G 5 0 R >> >> /N << /SMask /None >> >> >>",
        };
        pdf.AddStream("/Type /XObject /Subtype /Form /BBox [0 0 50 50]", "0 0 50 50 re f");
        var (_, session) = Draw(pdf, "/S gs /N gs 0 0 10 10 re f");
        Assert.That(session.All<OpenedGroup>(), Is.Empty);
    }

    [Test]
    public void ContentOfAGroupThatIsOffIsNotDrawn_ButItsClipsStillCut()
    {
        var pdf = new TestPdf
        {
            CatalogExtra = "/OCProperties << /OCGs [5 0 R] /D << /OFF [5 0 R] >> >>",
            Resources = "<< /Properties << /L 5 0 R >> >>",
        };
        pdf.Add("<< /Type /OCG /Name (Layer) >>");
        var (_, session) = Draw(pdf, "/OC /L BDC 0 0 5 5 re f 0 0 9 9 re W n EMC 1 1 1 1 re f");
        var drawn = session.All<DrawnGeometry>().Single();
        Assert.That(drawn.Depth, Is.EqualTo(1));
    }

    [Test]
    public void AFormInAGroupThatIsOffIsNotDrawn()
    {
        var pdf = new TestPdf
        {
            CatalogExtra = "/OCProperties << /OCGs [6 0 R] /D << /OFF [6 0 R] >> >>",
            Resources = "<< /XObject << /F 5 0 R >> >>",
        };
        pdf.AddStream("/Type /XObject /Subtype /Form /BBox [0 0 10 10] /OC 6 0 R", "0 0 1 1 re f");
        pdf.Add("<< /Type /OCG /Name (Layer) >>");
        var (_, session) = Draw(pdf, "/F Do");
        Assert.That(session.Calls, Is.Empty);
    }

    [Test]
    public void AnImageIsItsUnitSquare_FirstRowOnTop()
    {
        var pdf = new TestPdf { Resources = "<< /XObject << /I 5 0 R >> >>" };
        pdf.AddStream("/Type /XObject /Subtype /Image /Width 2 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8",
            TestPdf.Latin1("ÿ\u0000\u0000\u0000\u0000ÿ"));
        var (_, session) = Draw(pdf, "q 20 0 0 10 5 5 cm /I Do Q");
        var image = session.All<DrawnImage>().Single();
        Assert.That(image.Image.Width, Is.EqualTo(2));
        // Image space (0, 0) is its top left corner as drawn: user (5, 15), the page's (5, 85).
        Assert.That(At(image.Transform, 0, 0), Is.EqualTo((5.0, 85.0)));
        Assert.That(At(image.Transform, 1, 1), Is.EqualTo((25.0, 95.0)));
    }

    [Test]
    public void AnImageDrawnTwiceIsDecodedOnce()
    {
        var pdf = new TestPdf { Resources = "<< /XObject << /I 5 0 R >> >>" };
        pdf.AddStream("/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8",
            TestPdf.Latin1("\u0080"));
        var (_, session) = Draw(pdf, "/I Do /I Do");
        var images = session.All<DrawnImage>().ToArray();
        Assert.That(images[0].Image, Is.SameAs(images[1].Image));
    }

    [Test]
    public void AStencilMaskPaintsTheFillColor()
    {
        var pdf = new TestPdf { Resources = "<< /XObject << /M 5 0 R >> >>" };
        pdf.AddStream("/Type /XObject /Subtype /Image /Width 8 /Height 1 /ImageMask true", TestPdf.Latin1("\u000f"));
        var (_, session) = Draw(pdf, "0 1 0 rg /M Do 0 0 1 rg /M Do");
        var images = session.All<DrawnImage>().ToArray();
        Assert.That(images, Has.Length.EqualTo(2));
        Assert.That(images[0].Image, Is.Not.SameAs(images[1].Image), "one picture for each color");
    }

    [Test]
    public void AnInlineImageIsDrawn()
    {
        var (_, session) = Draw(new TestPdf(), "q 10 0 0 10 0 0 cm BI /W 1 /H 1 /CS /G /BPC 8 ID \u0080 EI Q");
        Assert.That(session.All<DrawnImage>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void AnImageThatCannotBeDecodedIsToldAndTheRestDrawn()
    {
        var pdf = new TestPdf { Resources = "<< /XObject << /I 5 0 R >> >>" };
        pdf.AddStream("/Type /XObject /Subtype /Image /Width 0 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8",
            "x");
        var (page, session) = Draw(pdf, "/I Do 0 0 1 1 re f");
        Assert.That(session.All<DrawnGeometry>().Count(), Is.EqualTo(1));
        Assert.That(page.Problems, Is.Not.Empty);
    }

    [Test]
    public void AnAnnotationIsDrawnByItsAppearanceIntoItsRect_UnlessTheCallerDrawsIt()
    {
        var pdf = new TestPdf { PageExtra = "/Annots [5 0 R]" };
        pdf.Add("<< /Type /Annot /Subtype /Square /Rect [100 50 120 60] /AP << /N 6 0 R >> >>");
        pdf.AddStream("/Type /XObject /Subtype /Form /BBox [0 0 2 1]", "0 0 2 1 re f");
        var (_, session) = Draw(pdf, "");
        var drawn = session.All<DrawnGeometry>().Single();
        Assert.That(At(drawn.Transform, 0, 0), Is.EqualTo((100.0, 50.0)));
        Assert.That(At(drawn.Transform, 2, 1), Is.EqualTo((120.0, 40.0)));

        var (_, skipped) = Draw(pdf, "", new PdfPageDrawingOptions { SkipAnnotation = a => a.Subtype == "Square" });
        Assert.That(skipped.All<DrawnGeometry>(), Is.Empty);
    }

    [TestCase(2, false, false)]
    [TestCase(32, false, false)]
    [TestCase(36, false, true)]
    [TestCase(4, true, true)]
    [TestCase(0, true, false)]
    public void AnAnnotationIsShownOrPrintedByItsFlags(int flags, bool shown, bool printed)
    {
        var pdf = new TestPdf { PageExtra = "/Annots [5 0 R]" };
        pdf.Add($"<< /Type /Annot /Subtype /Square /F {flags} /Rect [0 0 10 10] /AP << /N 6 0 R >> >>");
        pdf.AddStream("/Type /XObject /Subtype /Form /BBox [0 0 1 1]", "0 0 1 1 re f");
        Assert.That(Draw(pdf, "").Session.All<DrawnGeometry>().Any(), Is.EqualTo(shown));
        Assert.That(Draw(pdf, "", new PdfPageDrawingOptions { Print = true }).Session.All<DrawnGeometry>().Any(),
            Is.EqualTo(printed));
    }

    [Test]
    public void APageIsBuiltOffTheUIThread()
    {
        var pdf = new TestPdf();
        var document = pdf.Open("1 0 0 rg 0 0 10 10 re f");
        var page = Task.Run(() => PdfPageDrawing.Build(document, document.Pages[0])).Result;
        var session = new RecordingSession();
        page.Render(session, Matrix4x4<float>.Identity, null);
        Assert.That(session.All<DrawnGeometry>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void ABrokenContentStreamDrawsWhatComesBeforeTheBreak()
    {
        var (page, session) = Draw(new TestPdf(), "0 0 1 1 re f (unterminated");
        Assert.That(session.All<DrawnGeometry>().Count(), Is.EqualTo(1));
    }
}
