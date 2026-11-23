### Added

- `DockingArea.IsBusy`: while an application gets ready, an overlay (`DockingArea.BusyOverlay`, a
  `DockingBusyOverlay` the theme draws - a shade with a busy indicator) covers the area and every window torn out of
  it, a window opened meanwhile too, and the area is disabled under it: no pointer, key or focus reaches its panes, so
  nothing in them can be changed - one switch in place of disabling each control. The sandbox's Docking page has a
  Busy check box.

### Fixed

- A disabled `RibbonGallery` looked enabled: its cells kept their full color. A disabled cell is faded now, picture
  and name, as a ribbon button's icon is, in Fluent, Graphite and macOS.
