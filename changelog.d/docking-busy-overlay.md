### Added

- `DockingArea.IsBusy`: while an application gets ready, an overlay (`DockingArea.BusyOverlay`, a
  `DockingBusyOverlay` the theme draws - a shade with a busy indicator) covers the area and every window torn out of
  it, a window opened meanwhile too. It takes the pointer, and the keyboard focus from a field being typed in, so
  nothing in the panes under it can be changed - one switch in place of disabling each control. The sandbox's Docking
  page has a Busy check box.
