### Added

- Printing, first step: `Adamantium.UI.Core.Printing` - `IPrintDocument` (`Paginate` on a sheet, `DrawPage` into any
  `IDrawingSession`, in DIPs from the sheet's top left), `PrintPage` (paper, orientation, the margins a printer cannot
  reach), `PaperSize` (A3, A4, A5, B5, Letter, Legal, Tabloid, or any size in millimeters) and `PageOrientation`.
- `PdfPrintFile.Save`: a document printed to a PDF file - every page in vector on a sheet of the size asked for, a
  picture on every page written once; called off, it stops between pages. Checked by our reader and by MuPDF.

### Changed

- `PdfContentSession` writes a whole drawing, not only an annotation's paths:
  - linear and radial gradients, filled and stroked - repeated and reflected, mixed in OKLab when asked, their alpha a
    soft mask of the same shape;
  - pictures as images - a part of one, cut to rounded corners, an animation's frame - and a drawing's picture as the
    drawing itself, in vector;
  - tile brushes - a picture or a drawing - laid out as the renderer lays them (viewport, viewbox, stretch, alignment,
    turned about their center), once or as tiling patterns, mirrored for the flip modes, tinted, a picture's
    brightness, contrast and saturation changed as the shader changes them;
  - pens as the stroke shader lays them: a trim, dashes - fitted to a closed contour, offset and phased - each end of
    its own kind (flat, square, round, pointed, notched, bitten), a pen measured in the window's units as wide on the
    sheet at any scale; ends PDF has are its own, the rest drawn as shapes, concave ones bitten out;
  - colored meshes as shaded triangles, their alpha a mask of the same triangles;
  - a group faded, blended or masked is a transparency group laid down as one, as a layer is on screen - before, its
    opacity was multiplied into each thing drawn in it, so overlapping parts came out darker; a group's glyphs cut it;
  - a border of four widths is the ring between its edges - before, only the left width was drawn, all round;
  - a group's clip of nothing lets nothing through, as on screen - before, it cut nothing.

  `CloseGroups` ends the groups left open: a group drawn apart goes into the content as it ends. Not written yet - the
  raster at print resolution is for them: a visual brush, a brush laid along a pen's line, live surfaces, and a drawing
  tile's brightness, contrast and saturation.
- `ImageTiling` is `TileBrushLayout`, in `Adamantium.UI.Core.Media`: the renderer and the printed page lay a tile brush
  out by the same arithmetic.
