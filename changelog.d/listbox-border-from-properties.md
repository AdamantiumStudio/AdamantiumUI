### Fixed

- Fluent and macOS: a `ListBox`'s edge comes from its own `BorderBrush`, `BorderThickness` and `CornerRadius`, as in
  Graphite, so `BorderThickness="0"` gives a list with no edge. The defaults are the edges they drew before.
