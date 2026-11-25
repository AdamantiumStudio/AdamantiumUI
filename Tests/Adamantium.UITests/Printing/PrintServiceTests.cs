using System.IO;
using System.Threading;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Printing;
using Adamantium.UI.Printing;
using NUnit.Framework;

namespace Adamantium.UITests.Printing;

/// <summary>Saving to a PDF file: the name offered, and a place that will not take the file.</summary>
public class PrintServiceTests
{
    private sealed class Blank : IPrintDocument
    {
        public string Title => "Blank";

        public int Paginate(PrintPage page) => 1;

        public void DrawPage(int index, IDrawingSession session)
        {
        }
    }

    [TestCase("Report v1.2", "Report v1.2.pdf")]
    [TestCase("notes.txt", "notes.txt.pdf")]
    [TestCase("Plan.PDF", "Plan.PDF")]
    [TestCase("a/b", "a_b.pdf")]
    [TestCase("  ", "Document.pdf")]
    public void TheFileOffered_IsTheWholeTitle_ThenPdf(string title, string expected) =>
        Assert.That(PrintService.PdfFileNameOf(title), Is.EqualTo(expected));

    [Test]
    public void APlaceThatWillNotTakeTheFile_IsAFailure_AndLeavesNothingBehind()
    {
        var folder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(folder);
        try
        {
            var sheets = new PrintSheets(new Blank(), new PrintSettings(), PrinterCapabilities.Pdf);
            sheets.Paginate(sheets.Sheet);

            // A folder of that name stands where the file would go: the move over it fails.
            var path = Path.Combine(folder, "taken.pdf");
            Directory.CreateDirectory(path);

            Assert.That(PrintService.WritePdf(sheets, path, CancellationToken.None), Is.False);
            Assert.That(File.Exists(path + ".writing"), Is.False, "no half of a PDF left");

            var free = Path.Combine(folder, "free.pdf");
            Assert.That(PrintService.WritePdf(sheets, free, CancellationToken.None), Is.True);
            Assert.That(new FileInfo(free).Length, Is.GreaterThan(0));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
