### Added

- `TreeDataGrid.HeadersVisibility`: collapsed, the row of column headers goes and the rows start at the top, for a tree
  with columns whose meaning is plain without titles.

### Fixed

- An Auto column of a `TreeDataGrid` did not grow to a cell whose content was bound after it was built, and its cells
  stayed where they were when it did grow beside a star column. The next column stood over its cells. Now:
  - a cell of an Auto column measures its content unbounded and reports the width itself, however it is measured;
  - a width reported outside the grid's own pass lays the grid and its header out again;
  - a grown Auto column has the rows shown arrange their cells again.
