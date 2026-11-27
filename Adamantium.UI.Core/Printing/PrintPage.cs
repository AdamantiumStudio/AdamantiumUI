using System;
using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Printing;

/// <summary>The sheet a document is laid out on and printed to: the paper, which way up, and the margins the printer
/// cannot reach.</summary>
public sealed class PrintPage
{
    private const double MinimumContent = 96;

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

    /// <summary>The paper's blank edges a document laid out on the sheet keeps inside, in DIPs, the way the sheet is
    /// printed - never narrower than <see cref="PrintableMargins"/>; null to keep its own
    /// (<see cref="PrintMargins.Document"/>).</summary>
    public Thickness? Margins { get; init; }

    /// <summary>The widest each margin may be, in DIPs: an inch of the sheet is left between two opposite ones.</summary>
    public Thickness MaximumMargins
    {
        get
        {
            var size = Size;
            var across = Math.Max(0, (size.Width - MinimumContent) / 2);
            var down = Math.Max(0, (size.Height - MinimumContent) / 2);
            return new Thickness(across, down, across, down);
        }
    }

    /// <summary>What a document laid out on the sheet fills: the sheet inside <see cref="Margins"/>, or inside
    /// <see cref="PrintableMargins"/> for one that keeps its own.</summary>
    public Rect ContentArea
    {
        get
        {
            var size = Size;
            var margins = Margins ?? PrintableMargins;
            return new Rect(margins.Left, margins.Top, Math.Max(1, size.Width - margins.Left - margins.Right),
                Math.Max(1, size.Height - margins.Top - margins.Bottom));
        }
    }

    /// <summary>How fine a picture of what is not vector is made for this sheet - a shadow, a live 3D view - in dots to
    /// the inch: the printer's resolution, or the one chosen for a PDF file.</summary>
    public int RasterDotsPerInch { get; init; } = PrintSettings.DefaultRasterDotsPerInch;
}
