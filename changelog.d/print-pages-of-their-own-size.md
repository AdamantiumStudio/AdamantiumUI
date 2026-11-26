### Added

- `IPrintDocument.PageSize`: a document whose pages keep their own sizes - a PDF's - says each one's; the print
  dialog's scale, place and pages to a sheet work from it, and such a page is centred at its own size or a scale of its
  own. Null, as before, for a document laid out on the sheet.
- A page of its own size lying across on a sheet standing up, or the other way, is turned a quarter where it prints
  larger so - its top to the left - in the preview and on paper; at its own size or a scale of its own, only where it
  fits only so.
- `PdfPrintDocument` (`Adamantium.UI.Pdf`): the pages of a PDF document to print, each at its own size, its optional
  content by its print state and the annotations marked to be printed; the pages last read are kept for the preview.
