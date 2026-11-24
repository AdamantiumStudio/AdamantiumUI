### Fixed

- A `PropertyGrid` line shared by a list's items and read off each - `IsVisible="{Self Inspected.Shown}"`, or a nested
  `ItemsProperty` whose `ItemAction` is bound to its item - hung the grid when the items answered differently: pointing
  the line at each item asked for another build, every pass. What changes as the line is pointed at its item is read by
  that same pass and asks for nothing more.
