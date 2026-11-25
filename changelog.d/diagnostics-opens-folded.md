### Added

- `IOverlayAware.IsCollapsed`: an overlay window opens collapsed to its title when its view model says so, and folding it
  tells the view model. `Width`: its width, collapsed and expanded alike. `AutomationId`: what automation finds it by
  when its title changes.

### Changed

- An overlay's view model, if it is disposable, is disposed when its showing ends, as a closed document's is: an overlay's
  view model is made for one showing. A window closed closes the overlay windows over it.
- Fluent: an overlay window's title bar is lower (34) and its buttons narrower (28), as a docked panel's; the sizes are
  theme resources (`OverlayTitleBarHeight`, `DockHeaderButtonSize`).
- Sandbox: the diagnostics window opens collapsed at the bottom-left, its title showing the render and loop rates.

### Fixed

- An overlay window's long title ends in an ellipsis beside the buttons instead of running on under them.
