using System.IO;
using System.Linq;
using System.Text;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Pdf.Interpretation;
using NUnit.Framework;
using static Adamantium.UI.Pdf.Tests.PdfPageDrawingTests;

namespace Adamantium.UI.Pdf.Tests;

/// <summary>What one stream does stays in it - hidden content, text state, depth - and what hostile files ask for is
/// bounded.</summary>
public class PdfStreamBoundaryTests
{
    // A Type 3 font whose glyph 'a' is a 10 wide square, advancing 10 at size 1.
    private static TestPdf WithType3(string glyph = "100 0 d0 0 0 100 100 re f", string procDictionary = "")
    {
        var pdf = new TestPdf { Resources = "<< /Font << /T 5 0 R >> >>" };
        pdf.Add("<< /Type /Font /Subtype /Type3 /FontBBox [0 0 100 100] /FontMatrix [0.1 0 0 0.1 0 0] " +
                "/CharProcs << /a 6 0 R >> /Encoding << /Type /Encoding /Differences [97 /a] >> " +
                "/FirstChar 97 /LastChar 97 /Widths [100] >>");
        pdf.AddStream(procDictionary, glyph);
        return pdf;
    }

    [Test]
    public void ALongType3StringDrawsEveryGlyph_InOrder()
    {
        var text = new string('a', 50);
        var (page, session) = Draw(WithType3(), $"BT /T 1 Tf 0 50 Td ({text}) Tj ET");
        var glyphs = session.All<DrawnGeometry>().ToArray();
        Assert.That(glyphs, Has.Length.EqualTo(50));
        Assert.That(At(glyphs[0].Transform, 0, 0).Item1, Is.EqualTo(0).Within(1e-4));
        Assert.That(At(glyphs[49].Transform, 0, 0).Item1, Is.EqualTo(490).Within(1e-3));
        Assert.That(page.Problems, Is.Empty);
    }

    [Test]
    public void AType3GlyphLeavesThePagesTextWhereItWas()
    {
        var pdf = WithType3("100 0 d0 BT 500 500 Td ET 0 0 100 100 re f");
        var (_, session) = Draw(pdf, "BT /T 1 Tf 10 50 Td (a) Tj (a) Tj ET");
        var glyphs = session.All<DrawnGeometry>().ToArray();
        Assert.That(At(glyphs[1].Transform, 0, 0).Item1, Is.EqualTo(20).Within(1e-4));
    }

    [Test]
    public void InvisibleType3TextDrawsNothing()
    {
        var (_, session) = Draw(WithType3(), "BT /T 1 Tf 3 Tr (aa) Tj ET");
        Assert.That(session.Calls, Is.Empty);
    }

    [Test]
    public void AType3GlyphWithABoxIsNotCutByIt()
    {
        var (_, session) = Draw(WithType3(procDictionary: "/BBox [0 0 1 1]"), "BT /T 1 Tf (aaa) Tj ET");
        Assert.That(session.All<OpenedGroup>(), Is.Empty);
        Assert.That(session.All<DrawnGeometry>().Count(), Is.EqualTo(3));
    }

    [Test]
    public void HiddenContentLeftOpenInAFormEndsWithTheForm()
    {
        var pdf = new TestPdf
        {
            CatalogExtra = "/OCProperties << /OCGs [6 0 R] /D << /OFF [6 0 R] >> >>",
            Resources = "<< /XObject << /F 5 0 R >> >>",
        };
        pdf.AddStream("/Type /XObject /Subtype /Form /BBox [0 0 100 100] /Resources << /Properties << /L 6 0 R >> >>",
            "/OC /L BDC 0 0 1 1 re f");
        pdf.Add("<< /Type /OCG /Name (Off) >>");
        var (_, session) = Draw(pdf, "/F Do 5 5 1 1 re f");
        Assert.That(session.All<DrawnGeometry>().Count(), Is.EqualTo(1), "the page's own fill");
    }

    [Test]
    public void HiddenContentLeftOpenOnThePageDoesNotHideItsAnnotations()
    {
        var pdf = new TestPdf
        {
            CatalogExtra = "/OCProperties << /OCGs [5 0 R] /D << /OFF [5 0 R] >> >>",
            Resources = "<< /Properties << /L 5 0 R >> >>",
            PageExtra = "/Annots [6 0 R]",
        };
        pdf.Add("<< /Type /OCG /Name (Off) >>");
        pdf.Add("<< /Type /Annot /Subtype /Square /Rect [0 0 10 10] /AP << /N 7 0 R >> >>");
        pdf.AddStream("/Type /XObject /Subtype /Form /BBox [0 0 1 1]", "0 0 1 1 re f");
        var (_, session) = Draw(pdf, "/OC /L BDC 0 0 1 1 re f");
        Assert.That(session.All<DrawnGeometry>().Count(), Is.EqualTo(1), "the annotation");
    }

    [Test]
    public void AStrayEmcInACellDoesNotEndThePagesHiddenContent()
    {
        var pdf = new TestPdf
        {
            CatalogExtra = "/OCProperties << /OCGs [6 0 R] /D << /OFF [6 0 R] >> >>",
            Resources = "<< /Pattern << /P 5 0 R >> /Properties << /L 6 0 R >> >>",
        };
        pdf.AddStream("/PatternType 1 /PaintType 1 /TilingType 1 /BBox [0 0 4 4] /XStep 4 /YStep 4 /Resources << >>",
            "EMC 0 0 2 2 re f");
        pdf.Add("<< /Type /OCG /Name (Off) >>");
        var (_, session) = Draw(pdf, "/OC /L BDC /Pattern cs /P scn 0 g 5 5 1 1 re f EMC");
        Assert.That(session.All<DrawnGeometry>(), Is.Empty);
    }

    [Test]
    public void APatternChosenWhileHiddenIsWholeWhenUsedLater()
    {
        var pdf = new TestPdf
        {
            CatalogExtra = "/OCProperties << /OCGs [6 0 R] /D << /OFF [6 0 R] >> >>",
            Resources = "<< /Pattern << /P 5 0 R >> /Properties << /L 6 0 R >> >>",
        };
        pdf.AddStream("/PatternType 1 /PaintType 1 /TilingType 1 /BBox [0 0 4 4] /XStep 4 /YStep 4 /Resources << >>",
            "0 0 2 2 re f");
        pdf.Add("<< /Type /OCG /Name (Off) >>");
        var (_, session) = Draw(pdf, "/OC /L BDC /Pattern cs /P scn EMC 0 0 50 50 re f");
        var cell = new RecordingSession();
        ((DrawingBrush)session.All<DrawnGeometry>().Single().Brush).Drawing.Render(cell,
            Adamantium.Mathematics.Matrix4x4<float>.Identity, null);
        Assert.That(cell.All<DrawnGeometry>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void ASoftMaskSetWhileHiddenHasItsPicture()
    {
        var pdf = new TestPdf
        {
            CatalogExtra = "/OCProperties << /OCGs [6 0 R] /D << /OFF [6 0 R] >> >>",
            Resources = "<< /ExtGState << /G << /SMask << /S /Alpha /G 5 0 R >> >> >> /Properties << /L 6 0 R >> >>",
        };
        pdf.AddStream("/Type /XObject /Subtype /Form /BBox [0 0 50 50]", "0 0 50 50 re f");
        pdf.Add("<< /Type /OCG /Name (Off) >>");
        var (_, session) = Draw(pdf, "/OC /L BDC /G gs EMC 0 0 50 50 re f");
        var mask = new RecordingSession();
        var group = session.All<OpenedGroup>().Single();
        group.Options.Mask.Render(mask, group.Options.MaskTransform, null);
        Assert.That(mask.All<DrawnGeometry>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void AnImageTakesTheFillOpacity()
    {
        var pdf = new TestPdf { Resources = "<< /XObject << /I 5 0 R >> /ExtGState << /G << /ca 0.5 >> >> >>" };
        pdf.AddStream("/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8",
            TestPdf.Latin1("\u0080"));
        var (_, session) = Draw(pdf, "/G gs 10 0 0 10 0 0 cm /I Do");
        var group = session.All<OpenedGroup>().Single();
        Assert.That(group.Options.Opacity, Is.EqualTo(0.5));
        Assert.That(session.All<DrawnImage>().Single().Depth, Is.EqualTo(1));
    }

    [Test]
    public void AStencilInManyColorsKeepsAFewPictures_AndMasksTheRest()
    {
        var pdf = new TestPdf { Resources = "<< /XObject << /M 5 0 R >> >>" };
        pdf.AddStream("/Type /XObject /Subtype /Image /Width 8 /Height 1 /ImageMask true", TestPdf.Latin1("\u000f"));
        var contents = new StringBuilder();
        for (var i = 0; i < 20; i++)
        {
            contents.Append(System.FormattableString.Invariant($"{i / 20.0:F2} 0 0 rg /M Do "));
        }

        var (_, session) = Draw(pdf, contents.ToString());
        Assert.That(session.All<DrawnImage>().Select(i => i.Image).Distinct().Count(), Is.EqualTo(8));
        var masked = session.All<OpenedGroup>().ToArray();
        Assert.That(masked, Has.Length.EqualTo(12));
        Assert.That(masked.All(g => g.Options.MaskKind == MaskKind.Alpha && g.Options.Mask != null), Is.True);
    }

    [Test]
    public void AStencilFilledWithAPatternPaintsItThroughTheStencil()
    {
        var pdf = new TestPdf { Resources = "<< /XObject << /M 5 0 R >> /Pattern << /P 6 0 R >> >>" };
        pdf.AddStream("/Type /XObject /Subtype /Image /Width 8 /Height 1 /ImageMask true", TestPdf.Latin1("\u000f"));
        pdf.AddStream("/PatternType 1 /PaintType 1 /TilingType 1 /BBox [0 0 4 4] /XStep 4 /YStep 4 /Resources << >>",
            "0 0 2 2 re f");
        var (_, session) = Draw(pdf, "/Pattern cs /P scn 10 0 0 10 0 0 cm /M Do");
        Assert.That(session.All<OpenedGroup>().First().Options.Mask, Is.Not.Null);
        Assert.That(session.All<DrawnGeometry>().Single().Brush, Is.InstanceOf<DrawingBrush>());
    }

    [Test]
    public void ClipsNestedPastTheLimitAreNotApplied_AndSaid()
    {
        var contents = new StringBuilder();
        for (var i = 0; i < PdfPageInterpreter.MaxOpenGroups + 50; i++)
        {
            contents.Append("q 0 0 1 1 re W n ");
        }

        var (page, session) = Draw(new TestPdf(), contents.ToString());
        Assert.That(session.DeepestGroup, Is.LessThan(PdfPageInterpreter.MaxOpenGroups));
        Assert.That(page.Problems.Any(p => p.Contains("open at once")), Is.True);
    }

    [Test]
    public void AFunctionShadingPaintedOftenIsSampledOnce()
    {
        var pdf = new TestPdf { Resources = "<< /Shading << /S 5 0 R >> >>" };
        pdf.Add("<< /ShadingType 1 /ColorSpace /DeviceGray /Function 6 0 R >>");
        pdf.AddStream("/FunctionType 4 /Domain [0 1 0 1] /Range [0 1]", "{ add 2 div }");
        var (_, session) = Draw(pdf, string.Concat(Enumerable.Repeat("/S sh ", 10)));
        Assert.That(session.All<DrawnImage>().Select(i => i.Image).Distinct().Count(), Is.EqualTo(1));
    }

    [Test]
    public void APatternWhoseMatrixFlattensEverythingPaintsNothing()
    {
        var pdf = new TestPdf { Resources = "<< /Pattern << /P 5 0 R >> >>" };
        pdf.AddStream("/PatternType 1 /PaintType 1 /TilingType 1 /BBox [0 0 4 4] /XStep 4 /YStep 4 " +
                      "/Matrix [0 0 0 0 0 0] /Resources << >>", "0 0 2 2 re f");
        var (_, session) = Draw(pdf, "/Pattern cs /P scn 0 0 50 50 re f");
        Assert.That(session.Calls, Is.Empty);
    }

    [Test]
    public void ThePageIsCutToItsCropBox()
    {
        var document = new TestPdf { PageExtra = "/CropBox [10 10 50 50]" }.Open("0 0 200 100 re f");
        var page = PdfPageDrawing.Build(document, document.Pages[0]);
        var session = new RecordingSession();
        page.Render(session, Adamantium.Mathematics.Matrix4x4<float>.Identity, null);
        var crop = (RectangleGeometry)session.All<OpenedGroup>().First().Options.Clip;
        Assert.That(crop.Rect, Is.EqualTo(new Adamantium.UI.Core.Rect(0, 0, 40, 40)));
        Assert.That(session.All<DrawnGeometry>().Single().Depth, Is.EqualTo(1));
    }
}
