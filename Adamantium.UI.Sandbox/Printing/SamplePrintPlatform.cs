using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Printing;

namespace Adamantium.UI.Sandbox.Printing;

/// <summary>Printers made up for trying the print dialog where no system printing is written yet: a laser printer that
/// prints both sides in gray, a photo printer with trays and paper types, and a small one out of paper - each offering
/// what it can, so the dialog's rows come and go with the printer. Printing to them sends nothing anywhere.</summary>
internal sealed class SamplePrintPlatform : IPrintPlatform
{
    internal const string Laser = "sample-laser";
    internal const string Photo = "sample-photo";
    internal const string Small = "sample-small";
    internal const string GlossyPhoto = "Glossy photo";

    /// <summary>What was last "printed": the printer and the sheets.</summary>
    public string LastJob { get; private set; }

    public Task<IReadOnlyList<Printer>> FindPrintersAsync(CancellationToken cancellation) =>
        Task.FromResult<IReadOnlyList<Printer>>(
        [
            new Printer(Laser, "Office laser (sample)", isDefault: true),
            new Printer(Photo, "Photo inkjet (sample)", queuedJobs: 2, state: PrinterState.Printing),
            new Printer(Small, "Small label printer (sample)", state: PrinterState.OutOfPaper),
        ]);

    public Task<PrinterCapabilities> CapabilitiesAsync(Printer printer, CancellationToken cancellation) =>
        Task.FromResult(printer.Id switch
        {
            Laser => new PrinterCapabilities
            {
                Papers = [PaperSize.A4, PaperSize.A5, PaperSize.Letter, PaperSize.Legal],
                DefaultPaper = PaperSize.A4,
                MaxCopies = 999,
                Collates = true,
                Duplex = [DuplexMode.OneSided, DuplexMode.LongEdge, DuplexMode.ShortEdge],
                Colors = [PrintColorMode.Grayscale],
                Qualities = [new PrintQuality(600, 600), new PrintQuality(1200, 1200)],
                DefaultQuality = new PrintQuality(600, 600),
                Trays = ["Automatic", "Tray 1", "Manual feed"],
                Margins = new Dictionary<string, Thickness>
                {
                    [PaperSize.A4.Name] = new(16), [PaperSize.A5.Name] = new(16),
                    [PaperSize.Letter.Name] = new(16), [PaperSize.Legal.Name] = new(16),
                },
            },
            Photo => new PrinterCapabilities
            {
                Papers = [PaperSize.A4, PaperSize.Letter, PaperSize.Millimeters("10 × 15 cm", 100, 150)],
                DefaultPaper = PaperSize.A4,
                MaxCopies = 99,
                Duplex = [DuplexMode.OneSided, DuplexMode.LongEdge],
                Colors = [PrintColorMode.Color, PrintColorMode.Grayscale],
                Qualities = [new PrintQuality(300, 300), new PrintQuality(1200, 1200), new PrintQuality(4800, 1200)],
                DefaultQuality = new PrintQuality(1200, 1200),
                Trays = ["Rear tray", "Cassette"],
                MediaTypes = ["Plain paper", GlossyPhoto, "Matte photo"],
            },
            _ => new PrinterCapabilities
            {
                Papers = [PaperSize.Millimeters("62 × 100 mm", 62, 100)],
                DefaultPaper = PaperSize.Millimeters("62 × 100 mm", 62, 100),
            },
        });

    /// <summary>The samples have no drivers of their own, so no settings window: never asked.</summary>
    public PrintSettings EditDriverSettings(PrinterCapabilities capabilities, PrintSettings settings) => null;

    /// <summary>As a printer driver would: glossy photo paper is printed on one side only.</summary>
    public PrintSettings Reconcile(PrinterCapabilities capabilities, PrintSettings settings) =>
        settings.MediaType == GlossyPhoto && settings.Duplex != DuplexMode.OneSided
            ? settings with { Duplex = DuplexMode.OneSided }
            : settings;

    public Task PrintAsync(IPrintDocument sheets, PrintSettings settings, IProgress<PrintProgress> progress,
        CancellationToken cancellation)
    {
        var count = sheets.Paginate(null);
        LastJob = $"{settings.Printer.Name}: {count} × {settings.Copies}";
        return Task.CompletedTask;
    }
}
