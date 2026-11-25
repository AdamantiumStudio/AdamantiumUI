### Added

- Printing, second step: the print dialog.
  - `PrintDialog` (in `Adamantium.UI.Controls.Printing`, templated in Fluent, Graphite and macOS): a preview of the
    sheets beside the options. The platform's own - which pages (all, the current one, or written out as "1-3, 5"),
    which way up, the paper, the scale, pages to a sheet - are there for every printer and the PDF file; each of the
    printer's - copies and their order, two sides, color, quality, paper source, paper type - shows only when the printer
    chosen offers more than one choice of it, offering only those. Choices that cannot go together are put right by the
    system, and the dialog shows what came back; a range that is no pages stops printing until it is.
  - `PrintPreview`: one sheet as it will be printed, drawn by the same layout that goes to the printer or the file.
  - `IPrintService.PrintAsync(document, currentPage)`: shows the dialog in a window of its own through the dialog
    service, then prints - to the system's printer, or into a PDF file the user names, written beside it and moved over
    it once whole.
  - `Adamantium.UI.Core.Printing`: `Printer` (`Printer.Pdf` for the file), `PrinterState`, `PrinterCapabilities`,
    `PrintSettings` (`FitTo` a printer), `PageRange`, `DuplexMode`, `PrintColorMode`, `PrintQuality`, `PrintScaling`,
    `PrintProgress`, `PrintResult`, and `IPrintPlatform` - the system's printers, registered as
    `SystemPrinting.Platform`; none yet, so the PDF file alone is offered.
  - `PrintSheets`: a document laid out on sheets as the dialog's own options say - its pages scaled into the printer's
    reach, several to a sheet in whichever grid shows them largest.
  - The sandbox's Printing page: a sample document, and sample printers that each offer different things.
