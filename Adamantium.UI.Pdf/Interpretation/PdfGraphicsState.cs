using Adamantium.Pdf.Document;
using Adamantium.Pdf.Fonts;
using Adamantium.Pdf.Graphics;
using Adamantium.UI.Core.Media.Drawings;

namespace Adamantium.UI.Pdf.Interpretation;

/// <summary>The graphics state a page is drawn with (ISO 32000-2, 8.4), saved by <c>q</c> and brought back by
/// <c>Q</c>. <see cref="Ctm"/> maps user space to the page's units - the page's own placement is in it from the start.
/// </summary>
internal sealed class PdfGraphicsState
{
    public PdfMatrix Ctm = PdfMatrix.Identity;

    public PdfPaint Fill;
    public PdfPaint Stroke;

    public double LineWidth = 1;
    public int LineCap;
    public int LineJoin;
    public double MiterLimit = 10;
    public PdfDashPattern Dash = PdfDashPattern.Solid;

    public double FillAlpha = 1;
    public double StrokeAlpha = 1;
    public PdfBlendMode BlendMode;

    /// <summary>The soft mask's picture in the page's units, null for none.</summary>
    public Drawing SoftMask;
    public PdfSoftMaskKind SoftMaskKind;
    public (double R, double G, double B) SoftMaskBackdrop;

    public double CharacterSpacing;
    public double WordSpacing;
    public double HorizontalScale = 1;
    public double Leading;
    public PdfFont Font;
    public double FontSize;
    public double Rise;
    public int TextRenderMode;

    /// <summary>How many groups are open while this state holds - the ones its clips opened, and every one before:
    /// <c>Q</c> closes those the state it brings back did not have.</summary>
    public int OpenGroups;

    public PdfGraphicsState Copy() => (PdfGraphicsState)MemberwiseClone();

    /// <summary>Whether a drawn object needs a group of its own: a blend mode or a soft mask acts on each object apart.
    /// </summary>
    public bool NeedsObjectGroup => BlendMode != PdfBlendMode.Normal || SoftMask != null;
}
