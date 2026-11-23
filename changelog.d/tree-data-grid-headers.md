### Added

- `TreeDataGrid.HeadersVisibility`: collapsed, the row of column headers goes and the rows start at the top, for a tree
  with columns whose meaning is plain without titles.

### Fixed

- An Auto column of a `TreeDataGrid` did not grow when a row was measured again on its own, as one whose text is bound
  a frame after it is built is: the next column stood over its cells. The grid now lays its columns out again.
