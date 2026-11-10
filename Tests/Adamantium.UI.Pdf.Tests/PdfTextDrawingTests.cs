using System.IO;
using System.Linq;
using NUnit.Framework;
using static Adamantium.UI.Pdf.Tests.PdfPageDrawingTests;

namespace Adamantium.UI.Pdf.Tests;

/// <summary>Text as runs of glyphs placed by their font's widths, in every rendering mode; Type 3 glyphs as content.
/// </summary>
public class PdfTextDrawingTests
{
    // An embedded TrueType font whose A is 600 wide and B 700, by its /Widths.
    private static TestPdf WithFont()
    {
        var pdf = new TestPdf { Resources = "<< /Font << /F 5 0 R >> >>" };
        pdf.Add("<< /Type /Font /Subtype /TrueType /BaseFont /SourceSans3-Bold /FirstChar 65 /LastChar 66 " +
                "/Widths [600 700] /Encoding /WinAnsiEncoding /FontDescriptor 6 0 R >>");
        pdf.Add("<< /Type /FontDescriptor /FontName /SourceSans3-Bold /Flags 32 /FontFile2 7 0 R >>");
        pdf.AddStream("", File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts",
            "SourceSans3-Bold.ttf")));
        return pdf;
    }

    [Test]
    public void AStringIsOneRun_EachGlyphAtItsWidth()
    {
        var (_, session) = Draw(WithFont(), "BT /F 10 Tf 5 5 Td (AB) Tj ET");
        var run = session.All<DrawnGlyphs>().Single();
        Assert.That(run.Run.FontSize, Is.EqualTo(10));
        Assert.That(run.Run.Glyphs.Select(g => g.X), Is.EqualTo(new[] { 0.0, 6 }).Within(1e-9));
        Assert.That(At(run.Transform, 0, 0), Is.EqualTo((5.0, 95.0)));
        // y down in the run is down on the page.
        Assert.That(At(run.Transform, 0, 1).Item2, Is.EqualTo(96).Within(1e-5));
        Assert.That(run.Fill, Is.Not.Null);
        Assert.That(run.Stroke, Is.Null);
    }

    [Test]
    public void AdjustmentsSpacingAndScaleMoveTheGlyphs()
    {
        var (_, session) = Draw(WithFont(), "BT /F 10 Tf 2 Tc [(A) -500 (B)] TJ ET BT /F 10 Tf 0 Tc 50 Tz (AB) Tj ET");
        var runs = session.All<DrawnGlyphs>().ToArray();
        // 6 for A, 2 of spacing, 5 for the -500.
        Assert.That(runs[0].Run.Glyphs[1].X, Is.EqualTo(13).Within(1e-9));
        // At half the width the run is squeezed by its matrix, its x still in units of the size.
        Assert.That(runs[1].Run.Glyphs[1].X, Is.EqualTo(6).Within(1e-9));
        Assert.That(At(runs[1].Transform, 6, 0).Item1, Is.EqualTo(3).Within(1e-5));
    }

    [Test]
    public void TheTextMatrixMovesPastWhatWasShown()
    {
        var (_, session) = Draw(WithFont(), "BT /F 10 Tf (A) Tj (B) Tj ET");
        var runs = session.All<DrawnGlyphs>().ToArray();
        Assert.That(At(runs[1].Transform, 0, 0).Item1, Is.EqualTo(6).Within(1e-5));
    }

    [TestCase(0, true, false)]
    [TestCase(1, false, true)]
    [TestCase(2, true, true)]
    [TestCase(3, false, false)]
    public void TheRenderingModeFillsStrokesOrHides(int mode, bool fills, bool strokes)
    {
        var (_, session) = Draw(WithFont(), $"BT /F 10 Tf {mode} Tr (A) Tj ET");
        var run = session.All<DrawnGlyphs>().SingleOrDefault();
        Assert.That(run != null && run.Fill != null, Is.EqualTo(fills));
        Assert.That(run != null && run.Stroke != null, Is.EqualTo(strokes));
    }

    [Test]
    public void ClippingTextCutsWhatFollowsTheBlock()
    {
        var (_, session) = Draw(WithFont(), "BT /F 10 Tf 7 Tr (AB) Tj ET 0 0 50 50 re f");
        Assert.That(session.All<DrawnGlyphs>(), Is.Empty);
        var group = session.All<OpenedGroup>().Single();
        Assert.That(group.Options.TextClip, Has.Count.EqualTo(1));
        Assert.That(session.All<DrawnGeometry>().Single().Depth, Is.EqualTo(1));
    }

    [Test]
    public void AStrokedRunsPenIsInUserSpace()
    {
        var (_, session) = Draw(WithFont(), "2 w BT /F 1 Tf 10 0 0 10 0 0 Tm 1 Tr (A) Tj ET");
        // The run's units are text space, ten times user space here: a 2 wide line is 0.2 of them.
        Assert.That(session.All<DrawnGlyphs>().Single().Stroke.Thickness, Is.EqualTo(0.2).Within(1e-9));
    }

    [Test]
    public void AFontThatCannotBeReadIsToldAndItsTextLeftOut()
    {
        var (page, session) = Draw(new TestPdf(), "BT /Missing 10 Tf (A) Tj ET 0 0 1 1 re f");
        Assert.That(session.All<DrawnGlyphs>(), Is.Empty);
        Assert.That(page.Problems.Any(p => p.Contains("Missing")), Is.True);
        Assert.That(session.All<DrawnGeometry>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void AType3GlyphIsItsContentStream_PlacedByTheFontMatrix()
    {
        var pdf = new TestPdf { Resources = "<< /Font << /T 5 0 R >> >>" };
        pdf.Add("<< /Type /Font /Subtype /Type3 /FontBBox [0 0 10 10] /FontMatrix [0.1 0 0 0.1 0 0] " +
                "/CharProcs << /a 6 0 R >> /Encoding << /Type /Encoding /Differences [97 /a] >> " +
                "/FirstChar 97 /LastChar 97 /Widths [100] >>");
        pdf.AddStream("", "100 0 d0 0 0 10 10 re f");
        var (_, session) = Draw(pdf, "1 0 0 rg BT /T 1 Tf 20 20 Td (aa) Tj ET");
        var glyphs = session.All<DrawnGeometry>().ToArray();
        Assert.That(glyphs, Has.Length.EqualTo(2));
        Assert.That(At(glyphs[0].Transform, 0, 0), Is.EqualTo((20.0, 80.0)));
        Assert.That(At(glyphs[0].Transform, 10, 10).Item1, Is.EqualTo(21).Within(1e-5));
        Assert.That(At(glyphs[1].Transform, 0, 0).Item1, Is.EqualTo(30).Within(1e-5));
        Assert.That(((Core.Media.SolidColorBrush)glyphs[1].Brush).Color.R, Is.EqualTo(255));
    }
}
