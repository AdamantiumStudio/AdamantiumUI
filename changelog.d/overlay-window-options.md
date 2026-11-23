### Added

- `OverlayWindow.ShowActivated`: when false, the window is shown without taking the keyboard or becoming the active
  one, and closing it hands neither on to the window behind it.
- `OverlayWindow.CanCollapse` and `IsCollapsed`: a title-bar button collapses the window to its title bar and back. It keeps
  its width while collapsed. Collapsing coerces its size rather than writing it, so a binding, a style or a size set
  while collapsed stays and shows once it is expanded. The macOS theme uses the yellow light for it. `IOverlayAware.CanCollapse`
  (false by default) turns it on for a window shown from a view model.

### Changed

- An `OverlayWindow`'s smallest size is its own `MinWidth` and `MinHeight` (300 and 120 by default; 260 wide in
  Graphite), for layout and for its resize grip alike. They used to be fixed in code and in each theme's template.
