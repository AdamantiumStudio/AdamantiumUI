### Added

- `OverlayStartupLocation.TopLeft`, `BottomLeft` and `BottomRight`, beside `TopRight`: an overlay window opens at that
  corner of its parent window, `Left` and `Top` the room from the corner's edges. It keeps to the corner as the parent
  resizes, and a new `Left` or `Top` moves it from there, until it is dragged or resized - or, at a bottom corner,
  collapsed; from then on they are its top-left.
- `Popup.RelativeCorner`: placed `Relative`, the corner of the target its offsets are measured from, the popup's same
  corner standing there - on a zoomed target too.
- Sandbox: the Navigation page chooses where its overlay window opens.
