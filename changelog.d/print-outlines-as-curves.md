### Changed

- Glyph outlines - text printed or saved by its outlines, and a PDF page's glyph runs (`GlyphRun.Outline`) - are built as
  curves alone, with no figure and segment objects: a page of 5400 characters in a gradient saves as PDF in 0.16 s and
  93 MB, where it took 0.58 s and 198 MB.
