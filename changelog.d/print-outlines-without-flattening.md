### Fixed

- Text printed or saved by its outlines - in a gradient or tile, a synthesized bold, a font that may not be embedded -
  no longer works out its outlines' bounds by flattening every curve into points, which no writer read: a page of such
  text saves as PDF in half the time and a sixth of the memory (1.13 s and 1.16 GB a page of 5400 characters, now
  0.58 s and 0.2 GB).
