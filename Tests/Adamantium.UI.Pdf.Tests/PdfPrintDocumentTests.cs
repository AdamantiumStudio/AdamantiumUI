using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Printing;
using NUnit.Framework;

namespace Adamantium.UI.Pdf.Tests;

/// <summary>A PDF's pages to print: each at its own size, in DIPs, turned by its <c>/Rotate</c>; drawn as they are
/// printed.</summary>
public class PdfPrintDocumentTests
{
    [Test]
    public void APage_IsItsOwnSize_InDips_TurnedByItsRotate()
    {
        var document = new TestPdf { MediaBox = "[0 0 216 72]", PageExtra = "/Rotate 90" }.Open("");
        var printed = new PdfPrintDocument(document, "Turned");

        Assert.That(printed.Paginate(new PrintPage(PaperSize.A4)), Is.EqualTo(1));
        Assert.That(printed.PageSize(0), Is.EqualTo(new Size<double>(96, 288)), "an inch across, three down");
    }

    [TestCase(4, true)]
    [TestCase(0, false)]
    public void AnAnnotation_IsDrawnOnlyWhenMarkedToBePrinted(int flags, bool printed)
    {
        var pdf = new TestPdf { PageExtra = "/Annots [5 0 R]" };
        pdf.Add($"<< /Type /Annot /Subtype /Square /F {flags} /Rect [0 0 10 10] /AP << /N 6 0 R >> >>");
        pdf.AddStream("/Type /XObject /Subtype /Form /BBox [0 0 1 1]", "0 0 1 1 re f");
        var session = new RecordingSession();

        new PdfPrintDocument(pdf.Open(""), "Marked").DrawPage(0, session);

        Assert.That(session.Calls.OfType<DrawnGeometry>().Any(), Is.EqualTo(printed));
    }
}
