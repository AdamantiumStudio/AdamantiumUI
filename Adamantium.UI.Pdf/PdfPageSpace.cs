using Adamantium.Pdf.Document;

namespace Adamantium.UI.Pdf;

/// <summary>The space a page is drawn in, its sheet: the page as it is shown - turned by <c>/Rotate</c>, cut to its crop
/// box, scaled by <c>/UserUnit</c> - in units from its top left corner, y down.</summary>
public static class PdfPageSpace
{
    /// <summary>From the page's default user space - y up, from the crop box's corner - to its sheet. Its inverse takes
    /// what is drawn on the sheet back into the page.</summary>
    public static PdfMatrix ToSheet(PdfPage page)
    {
        var box = page.CropBox;
        var width = box.Width;
        var height = box.Height;
        var upright = new PdfMatrix(1, 0, 0, -1, -Math.Min(box.Left, box.Right), Math.Max(box.Top, box.Bottom));
        var turned = (((page.Rotation % 360) + 360) % 360) switch
        {
            90 => new PdfMatrix(0, 1, -1, 0, height, 0),
            180 => new PdfMatrix(-1, 0, 0, -1, width, height),
            270 => new PdfMatrix(0, -1, 1, 0, 0, width),
            _ => PdfMatrix.Identity,
        };
        var unit = page.UserUnit > 0 ? page.UserUnit : 1;
        return upright.Then(turned).Then(new PdfMatrix(unit, 0, 0, unit, 0, 0));
    }
}
