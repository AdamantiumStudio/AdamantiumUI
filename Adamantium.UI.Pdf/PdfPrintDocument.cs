using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.Pdf.Document;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Printing;

namespace Adamantium.UI.Pdf;

/// <summary>The pages of a PDF document to print, each at its own size, as they are printed: optional content by its
/// print state, the annotations marked to be printed. The document is read by this alone, one call at a time - the
/// preview, then the print job - so give it one nothing else reads. The pages last read are kept while this lives: let
/// go of it once printed.</summary>
public sealed class PdfPrintDocument : IPrintDocument
{
    // A point is 1/72 inch, a DIP 1/96.
    private const double DipsPerPoint = 96.0 / 72.0;

    // The pages last read: the preview draws a sheet again on every repaint, several pages to a sheet each.
    private const int PagesKept = 16;

    private static readonly PdfPageDrawingOptions ForPrint = new() { Print = true };

    private readonly PdfDocument _document;
    private readonly Dictionary<int, PdfPageDrawing> _read = new();
    private readonly Queue<int> _readOrder = new();

    public PdfPrintDocument(PdfDocument document, string title)
    {
        ArgumentNullException.ThrowIfNull(document);
        _document = document;
        Title = title ?? string.Empty;
    }

    public string Title { get; }

    /// <summary>The document's own pages, whatever the sheet: a PDF is not laid out again.</summary>
    public int Paginate(PrintPage page) => _document.Pages.Count;

    public Size<double>? PageSize(int index)
    {
        var page = _document.Pages[index];
        return new Size<double>(page.Width * DipsPerPoint, page.Height * DipsPerPoint);
    }

    public void DrawPage(int index, IDrawingSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        Read(index).Render(session, Matrix4x4.CreateScale((float)DipsPerPoint, (float)DipsPerPoint, 1f), null);
    }

    private PdfPageDrawing Read(int index)
    {
        if (_read.TryGetValue(index, out var kept))
        {
            return kept;
        }

        var drawing = PdfPageDrawing.Build(_document, _document.Pages[index], ForPrint);
        _read[index] = drawing;
        _readOrder.Enqueue(index);
        if (_readOrder.Count > PagesKept)
        {
            _read.Remove(_readOrder.Dequeue());
        }

        return drawing;
    }
}
