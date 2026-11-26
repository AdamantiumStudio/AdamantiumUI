### Added

- `ElementPrintDocument` prints an element as it shows: each element in it draws itself as on the screen, read-only,
  cut to its bounds and rounded corners, faded and masked as on the screen. `ElementFit.Width` (the default) shrinks a
  wide element to the width the printer reaches and runs a tall one on down the sheets; `ElementFit.None` keeps its own
  size and runs on across as well. Shadows, glows, soft edges, reflections and live surfaces are not printed yet.
