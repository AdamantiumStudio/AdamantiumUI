### Added

- `PdfPageDrawing.PrepareDetail` makes, on all the processor's cores, the copies of a page's long paths that
  `AtDetail` draws at a zoom, with their outlines and their fills' tiles, and `IsPreparedAt` tells whether they are
  made: a viewer makes them away from the window when the zoom comes to a new level, and draws the page as before
  meanwhile. Made at the first draw, they held the window for over half a second on a whole map.

### Changed

- A PDF page's long filled areas are drawn by fewer points while the page is shown small, as its long lines were
  (`PdfPageDrawing.AtDetail`): every point left out stays within a quarter of a pixel of the outline drawn, from a
  sixteenth of a point on - zoomed further in, few of an area's pieces lie under a pixel. A fill's cost on the GPU is
  the pieces of its outline under each pixel, which a whole page crowds by the hundred: the geologic map of Alaska
  shown whole takes 46 - 52 ms of the GPU a frame, not 66 - 79.
- A path the simplification would bring down to a dot or to an area of nothing - too small to see - is drawn by its
  farthest three or four points, not by all of them.
