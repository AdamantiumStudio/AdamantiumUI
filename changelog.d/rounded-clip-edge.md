### Fixed

- The edge of a rounded clip is one pixel wide, as a fill's is: along a straight side it faded the first row of pixels
  inside by a sixth.
- An element that clips round its corners keeps them while the render thread plays its transform: it clipped square.
