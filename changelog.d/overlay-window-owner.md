### Added

- `OverlayWindow.Owner`: the control a window belongs to, not the parent window as a whole.
  - While the owner is not shown - its tab switched away, it taken out of the tree or to another window - the window is
    hidden, no longer the active one, and the keyboard in it is let go; shown again, it is back where it was. A window
    hidden by anyone else stays hidden.
  - As the owner moves, the window goes with it, as far from it as it was.
- `OverlayWindow.StaysInsideOwner` (default true): dragged and resized only as far as the owner's edges, and moved in -
  made smaller where larger - as the owner shrinks or the window unfolds. False, it goes anywhere in the parent window.
- `OverlayWindow.PlacedByHand`: raised as the window is dragged by its title bar or resized by its corner, not as it is
  placed by `Left` and `Top` or kept with its owner.

### Fixed

- A pinned overlay window no longer covers a modal one: modal windows stay on top of the pinned.
