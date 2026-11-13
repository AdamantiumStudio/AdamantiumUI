using Adamantium.Pdf.Document;

namespace Adamantium.UI.Pdf.Interpretation;

/// <summary>A side of a mesh's patch or triangle by its points - a curve's four, a line's ends each twice - the same
/// whichever way it is walked, so two pieces sharing a side share one.</summary>
internal readonly record struct PdfMeshSide(PdfPoint First, PdfPoint Second, PdfPoint Third, PdfPoint Fourth)
{
    public static PdfMeshSide Of(PdfPoint start, PdfPoint control, PdfPoint otherControl, PdfPoint end)
    {
        var forward = start.X < end.X || (start.X == end.X && start.Y <= end.Y);
        return forward
            ? new PdfMeshSide(start, control, otherControl, end)
            : new PdfMeshSide(end, otherControl, control, start);
    }

    public static PdfMeshSide Line(PdfPoint start, PdfPoint end) => Of(start, start, end, end);
}
