using System;
using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Printing;

/// <summary>The sheet a document is laid out on and printed to: the paper, which way up, and the margins the printer
/// cannot reach.</summary>
public sealed class PrintPage
{
    public PrintPage(PaperSize paper, PageOrientation orientation = PageOrientation.Portrait,
        Thickness printableMargins = default)
    {
        ArgumentNullException.ThrowIfNull(paper);
        if (!(paper.Width > 0) || !(paper.Height > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(paper), "A sheet has a width and a height.");
        }

        Paper = paper;
        Orientation = orientation;
        PrintableMargins = printableMargins;
    }

    public PaperSize Paper { get; }

    public PageOrientation Orientation { get; }

    /// <summary>The sheet as it is printed, in DIPs: the paper's size, turned on its side for
    /// <see cref="PageOrientation.Landscape"/>.</summary>
    public Size<double> Size => Orientation == PageOrientation.Landscape
        ? new Size<double>(Paper.Height, Paper.Width)
        : new Size<double>(Paper.Width, Paper.Height);

    /// <summary>The edges of the sheet the printer leaves blank, in DIPs, the way the sheet is printed; none when saving
    /// to PDF.</summary>
    public Thickness PrintableMargins { get; }

    /// <summary>How fine a picture of what is not vector is made for this sheet - a shadow, a live 3D view - in dots to
    /// the inch: the printer's resolution, or the one chosen for a PDF file.</summary>
    public int RasterDotsPerInch { get; init; } = PrintSettings.DefaultRasterDotsPerInch;
}
