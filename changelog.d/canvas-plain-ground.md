### Fixed

- An `InfiniteCanvas` drew its ground again at every move of the camera, though a plain ground - no grid marks, no
  axes, as a `PdfViewer`'s - is the same at every camera; the frame was dirty, and the renderer walked everything on
  the plane again where the move alone is matrices. The ground is drawn again only when it follows the camera. Panning
  the geologic map of Alaska, the render thread spends a median 9 ms on a frame, not 27; zooming, 9 ms, not 20.
