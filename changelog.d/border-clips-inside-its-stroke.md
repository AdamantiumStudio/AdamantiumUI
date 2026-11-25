### Fixed

- A rounded `Border` with a stroke that clips (`ClipToBounds`) cut what it holds along its outer edge, so a filled child
  painted over the stroke at the corners - the accent frame of a docking pane lost there. It cuts along the stroke's
  inner edge; a clip given a `ClipCornerRadius` of its own is taken as given.