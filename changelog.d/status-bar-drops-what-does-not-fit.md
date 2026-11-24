### Added

- `StatusBar.Priority` and `StatusBar.Shrinks`, attached to a status bar's fields: how long a field stays as the bar
  narrows, and whether it narrows - a message cut short - before any field goes.

### Fixed

- A narrow status bar no longer draws its fields over one another: its fields are laid out by `StatusBarPanel`, from the
  left and - docked right - from the right as before. As the bar narrows, a field that shrinks gives up its width first;
  then the fields that do not fit go, the lowest `StatusBar.Priority` first, of the same priority the one written later,
  and a `Separator` left with nothing to stand between goes with them. A field gone is laid out wholly past the bar's
  end, read offscreen by automation; its own Visibility is left as its triggers set it.
- The web view's status bar cuts a long link short instead of letting it run under the zoom and the DevTools.
