### Added

- `TreeDataGrid.HeadersVisibility`: collapsed, the row of column headers goes and the rows start at the top, for a tree
  with columns whose meaning is plain without titles.
- `ITheme.AccentFillColorChanged`: the accent as a wash lighter than the selection's, derived from the accent like the
  rest of the ramp. A `TreeDataGrid` cell whose state is `Changed` - a debugger's value from one stop to the next - is
  washed in it across the whole cell, in every theme; search and selection still win over it.

### Fixed

- An Auto column of a `TreeDataGrid` did not grow to a cell whose content was bound after it was built, and when it did
  grow beside a star column its rows and header kept their cells where they stood: the next column stood over them.
  - A row's cell of an Auto column now measures its content unbounded and reports the width itself, however it comes
    to be measured. A column held under its widest by `MaxWidth` still measures at its width and trims there.
  - When a pass moves the column widths, the rows shown and the header are laid out again at them.
  - The strip for a new record and a cell being edited do not widen an Auto column.
