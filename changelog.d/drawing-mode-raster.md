### Added

- `UIComponent.DrawingMode`: `Vector` (the default) draws an element from its shapes every frame; `Raster` draws its
  subtree into one texture - what the window shows of it and a quarter as much again each way, at a scale a power of
  two from the camera's - and lays the texture down where the camera puts it, moved and scaled, until the scale goes a
  step further, the camera leaves what it holds or what it draws changes. For heavy content the camera moves over: the
  whole geologic map of Alaska zooms at 60 frames a second with the GPU at 0.6 ms a frame, against 30 frames and 11 ms
  drawn from its shapes.

### Fixed

- A group or mask drawn apart - a cut by a path, an opacity mask, a blend - kept textures of its own, a ring of them the
  size of what it covered, from frame to frame, though it is drawn and laid down within one frame. A page of hundreds of
  cut groups held over 3 GB of GPU memory after a few zoom steps and the application stopped drawing. They share one
  store now: the same page holds about 1 GB at most, drawn from its shapes 0.7 GB, not 1.2 GB.
- A fill's curves were packed for the shaders anew for every unit made from them, on the render thread: a page of
  thousands of paths spent over a second of it each time it was drawn again. They are packed once, and
  `Geometry.PrepareForDrawing` packs them away from the render thread with the tiles; the frame a PDF page's new detail
  level lands in takes 350 ms, not about a second.
- A picture baked off-screen (a pattern, a `DrawingImage` brush) waited for the whole GPU after its frame - the
  window's frame as well - so a page of a hundred patterns held the render thread for seconds. It waits for its own
  frame.
