### Added

- A `TreeDataGrid` cell editor's bindings with `UpdateSourceTrigger.Explicit` are written when the edit is committed and
  never when it is cancelled. A template column's editor that binds the row itself - a field writing a debugged value
  into a running game, say - can now be taken back with Escape; bound `LostFocus`, its leaving wrote it even on a
  cancel.
