using System.Collections.Generic;
using System.Linq;

namespace Adamantium.UI.Core.Printing;

/// <summary>What is printed and how: the dialog's choices. Some are the platform's own and work everywhere - which pages,
/// the paper's way up, the scale, pages to a sheet; the rest are the printer's, and mean something only where its
/// <see cref="PrinterCapabilities"/> offer them.</summary>
public sealed record PrintSettings
{
    /// <summary>How many pages a sheet may hold.</summary>
    public static IReadOnlyList<int> PagesPerSheetChoices { get; } = [1, 2, 4, 6, 9, 16];

    public Printer Printer { get; init; } = Printer.Pdf;

    public PageRange Range { get; init; } = PageRange.All;

    public PageOrientation Orientation { get; init; }

    public PaperSize Paper { get; init; } = PaperSize.A4;

    public PrintScaling Scaling { get; init; }

    /// <summary>The scale for <see cref="PrintScaling.Custom"/>, in percent: 10 to 400.</summary>
    public double ScalePercent { get; init; } = 100;

    /// <summary>How many pages each sheet holds, one of <see cref="PagesPerSheetChoices"/>.</summary>
    public int PagesPerSheet { get; init; } = 1;

    public int Copies { get; init; } = 1;

    /// <summary>Whether copies come in order - all of the first, then all of the second.</summary>
    public bool Collate { get; init; } = true;

    public DuplexMode Duplex { get; init; }

    public PrintColorMode Color { get; init; }

    public PrintQuality Quality { get; init; }

    public string Tray { get; init; }

    public string MediaType { get; init; }

    /// <summary>These settings with each of the printer's choices one <paramref name="capabilities"/> offers - a
    /// choice it does not offer goes back to its default - and the platform's own kept in their bounds.</summary>
    public PrintSettings FitTo(PrinterCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        var papers = capabilities.Papers.Count > 0 ? capabilities.Papers : PaperSize.Standard;
        return this with
        {
            Paper = papers.FirstOrDefault(paper => paper.Name == Paper?.Name) ??
                    papers.FirstOrDefault(paper => paper.Name == capabilities.DefaultPaper?.Name) ?? papers[0],
            ScalePercent = Math.Clamp(double.IsFinite(ScalePercent) ? ScalePercent : 100, 10, 400),
            PagesPerSheet = PagesPerSheetChoices.Contains(PagesPerSheet) ? PagesPerSheet : 1,
            Copies = Math.Clamp(Copies, 1, Math.Max(1, capabilities.MaxCopies)),
            Collate = capabilities.Collates && Collate,
            Duplex = capabilities.Duplex.Contains(Duplex) ? Duplex : DuplexMode.OneSided,
            Color = capabilities.Colors.Count == 0 || capabilities.Colors.Contains(Color)
                ? Color
                : capabilities.Colors[0],
            Quality = capabilities.Qualities.Contains(Quality) ? Quality : capabilities.DefaultQuality,
            Tray = capabilities.Trays.Contains(Tray) ? Tray : capabilities.Trays.FirstOrDefault(),
            MediaType = capabilities.MediaTypes.Contains(MediaType) ? MediaType : capabilities.MediaTypes.FirstOrDefault(),
        };
    }
}
