### Added

- Margins for printing: `PrintSettings.Margins` (`PrintMargins`: the document's own, normal, narrow, wide, none, or
  custom by `PrintSettings.CustomMargins`), offered by the print dialog under the paper. Custom shows a number box for
  each side, kept between what the printer reaches and what leaves room on the sheet, in millimeters or inches by the
  system's region; the preview draws the margins dashed while they are set. A document with margins of its own
  (`IPrintDocument.HasOwnMargins`, a PDF's pages) keeps them unless others are chosen; one without is laid out inside
  the normal ones, and finds them in `PrintPage.Margins` and `PrintPage.ContentArea`.
- `IPrintDocument.PreparePagesAsync`: the pages of the sheet drawn next made ready right before it - by the PDF file,
  the Windows and CUPS jobs and the preview, sheet by sheet - so a long document holds only those pages at a time.
- The print dialog inside a window, as a page of it: `PrintDialog.IsInline` puts "Print" and the copies at the head of
  the options and drops the bottom bar; `PrintDialog.Command` runs with a `PrintRequest` (the document, the settings,
  the current page) for a view model to hand to `IPrintService.PrintAsync(PrintRequest)`, which prints without asking.
  `IPrintService.Platform` gives the system's printers to show there. The sandbox has a "Print page" tab.

### Changed

- The print dialog's commands are read-only properties of the control, bound by `TemplateBinding`.
- `ElementPrintDocument` lays an element inside the paper's margins, normal ones unless others are chosen, rather than
  out to the printer's edge.
