using System.IO;
using System.Threading;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Writing;
using Adamantium.UI.Core.Printing;

namespace Adamantium.UI.Pdf;

/// <summary>A document printed to a PDF file instead of a printer: each page drawn as it is on screen, in vector -
/// shapes, gradients, pictures - on a sheet of the size asked for.</summary>
public static class PdfPrintFile
{
    /// <summary>The PDF's units to the inch; the document draws in DIPs, 96 to the inch.</summary>
    private const double PointsPerDip = 72.0 / 96.0;

    /// <summary>Lays <paramref name="document"/> out on <paramref name="page"/> and writes every page of it to
    /// <paramref name="output"/>, which is not closed; a document of no pages is one blank sheet, as a printer would
    /// give - a PDF of none is not opened by many readers. Called off, it stops between pages; what was written is not
    /// a whole file.</summary>
    public static void Save(IPrintDocument document, PrintPage page, Stream output,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(output);
        var count = document.Paginate(page);
        var blank = count <= 0;
        var writer = new PdfDocumentWriter(output)
        {
            Title = document.Title,
            Created = DateTimeOffset.Now,
        };
        var size = page.Size;
        var width = size.Width * PointsPerDip;
        var height = size.Height * PointsPerDip;
        // From DIPs down from the sheet's top left to points up from its bottom left.
        var toPage = new PdfMatrix(PointsPerDip, 0, 0, -PointsPerDip, 0, height);
        var pictures = new PdfPictures();
        for (var index = 0; index < count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var content = new PdfContentBuilder();
            var session = new PdfContentSession(content, toPage, pictures);
            document.DrawPage(index, session);
            session.CloseGroups();
            writer.AddPage(new PdfRectangle(0, 0, width, height), content);
        }

        if (blank)
        {
            writer.AddPage(new PdfRectangle(0, 0, width, height), new PdfContentBuilder());
        }

        writer.Finish();
    }
}
