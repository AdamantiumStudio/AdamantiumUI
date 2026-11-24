### Added

- `UIComponent.DrawingMode`: `Vector` (the default) draws an element from its shapes every frame; `Raster` draws its
  subtree into one texture - what the window shows of it and a quarter as much again each way, at a scale a power of
  two from the camera's - and lays the texture down where the camera puts it, moved and scaled, until the scale goes a
  step further, the camera leaves what it holds or what it draws changes. While the camera zooms the texture held is
  laid down stretched, and drawn anew once, when it stops; a frame that only moves the camera is replayed, not walked.
  For heavy content the camera moves over: on a map sheet of about 12,000 paths a frame moving the camera takes 0.7 ms
  on the render thread and 0.5 ms on the GPU.
- `UIComponent.RasterTileSize` and `RasterFrameBudget`: a raster's texture is drawn anew square by square (512 texels
  by default), nearest the middle of what is seen first, as many squares a frame as fit in the budget (4 ms by
  default), the texture it holds laid down until the last square is in. Each square passes over the draws it does not
  reach. The first texture an element ever holds is drawn whole.

### Fixed

- A group or mask drawn apart - a cut by a path, an opacity mask, a blend - kept textures of its own, a ring of them the
  size of what it covered, from frame to frame, though it is drawn and laid down within one frame. A page of hundreds of
  cut groups held over 3 GB of GPU memory after a few zoom steps and the application stopped drawing. They share one
  store now, which lets go of what a frame did not need, and the same page stays at 1.4-2.1 GB.
- A fill's curves were packed for the shaders anew for every unit made from them, on the render thread: a page of
  thousands of paths spent over a second of it each time it was drawn again. They are packed once, and
  `Geometry.PrepareForDrawing` packs them away from the render thread with the tiles; the frame a PDF page's new detail
  level lands in takes 350 ms, not about a second.
- A picture baked off-screen (a pattern, a `DrawingImage` brush) waited for the whole GPU after its frame - the
  window's frame as well - so a page of a hundred patterns held the render thread for seconds. It waits for its own
  frame, and the element showing a page of them is recorded again once, when the last one lands.
