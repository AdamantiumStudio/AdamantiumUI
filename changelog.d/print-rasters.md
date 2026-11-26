### Added

- What is not vector is printed as pictures the GPU makes at the print's raster resolution. An element with a shadow or
  a glow, or whose own drawing shows a live surface (a 3D view, a game) or paints with a visual brush, has its own
  drawing printed as a picture, its shadow past its edges with it; what it holds stays vector. A soft edge, a
  reflection, or a mask on such an element makes all it holds a picture. Pictures are made unfaded and faded as shapes
  are, as fine as the sheet shows each element (the sheet's scale and what enlarges it included), in squares of at most
  8192 pixels; one that cannot be made leaves its element printed from its shapes.
- `IPrintDocument.PrepareAsync` makes ready, after `Paginate` and before the first page, what drawing takes and cannot
  be made while drawing; every print job, the PDF file and the print preview call it - the preview at the screen's
  resolution. `ElementPrintDocument` makes its pictures there, again after each `Paginate`, and is `IDisposable` to
  free them.
- `PrintPage.RasterDotsPerInch`: a printer's resolution (at most 600 dpi), or for a PDF file the print dialog's
  "Raster resolution" (150, 300 or 600 dpi; 300 by default), back in all three themes.
- `IVisualRenderer.RequestSnapshot(visual, region, scale, alone, onReady)`: a region of a live element, past its edges
  too, at a scale, unfaded; its own drawing alone or with all it holds.

### Changed

- `PdfPrintFile.Save` is `PdfPrintFile.SaveAsync`: it makes the document ready between laying it out and drawing it.
- Saving to a PDF file that is called off returns `PrintResult.Canceled`.
