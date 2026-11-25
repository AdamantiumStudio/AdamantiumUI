### Added

- Printing, third step: Windows' printers.
  - The spooler's printers - local ones and connections, the default chosen, each with its state - are offered in the
    print dialog, registered as `SystemPrinting.Platform` at startup.
  - What a printer offers comes from its PrintCapabilities: the paper it takes (a standard size by its standard name,
    so A4 stays A4 from printer to printer), copies and their order, two sides, color, resolution, paper source and
    paper type. The margins of a paper are asked for when it is chosen. Choices that cannot go together are put right
    by the driver through its PrintTicket, and the dialog shows what came back.
  - "Printer settings…" in the dialog opens the driver's own window, for what the dialog does not offer; what it is
    left with is printed, the dialog's own options as it set them (`IPrintPlatform.EditDriverSettings`,
    `PrintSettings.DriverSettings`).
  - A job goes to the printer as XPS through Print Document Package, with its PrintTicket, the sheets written through
    the XPS object model in vector: paths, solid and gradient paint, pictures as PNG once a job, tiles, faded and cut
    groups. A pen XPS strokes as it is is its own; any other is laid out as the stroke shader lays it, as in PDF.
- The sandbox's Printing page offers the system's printers beside the sample ones.

### Changed

- How the stroke shader lays out a pen, the shapes of ink and arrows, a gradient's stops as drawn and their repeats,
  text as runs of glyph outlines, and a picture's pixels as embedded are worked out once, in
  `Adamantium.UI.Core.Graphics.Vector`, for the PDF and XPS writers both.
- The print dialog offers only the platform's own options when the system cannot say what a printer does, and the PDF
  file when it cannot list its printers.
