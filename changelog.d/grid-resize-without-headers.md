### Added

- A table that shows no headers still has its columns resized: the line between two columns is dragged in the rows,
  the resize cursor over it, and double-clicked to fit the column. The header and the rows share one drag
  (`TreeDataGrid.ColumnResize`) and one answer for where a column stands (`TreeDataGrid.ScreenXOf`).
- `TreeDataGrid.MarksReadOnlyCells`: off, cells that cannot be edited look as the rest - still not editable. On by
  default. The themes wash a cell by `DataGridCell.LooksReadOnly`, which is its `IsReadOnly` where the grid marks such
  cells.
