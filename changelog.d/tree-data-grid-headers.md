### Added

- `TreeDataGrid.HeadersVisibility`: collapsed, the row of column headers goes and the rows start at the top, for a tree
  with columns whose meaning is plain without titles.

### Fixed

- An Auto column of a `TreeDataGrid` did not grow to a cell whose content was bound after it was built, and when it did
  grow beside a star column its rows and header kept their cells where they stood: the next column stood over them.
  - A row's cell of an Auto column now measures its content unbounded and reports the width itself, however it comes
    to be measured. A column held under its widest by `MaxWidth` still measures at its width and trims there.
  - A width reported outside the grid's own pass lays out again the grid, the rows shown and the header.
  - The strip for a new record and a cell being edited do not widen an Auto column.
