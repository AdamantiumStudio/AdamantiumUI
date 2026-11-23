### Added

- A `TreeDataGrid` cell editor's bindings with `UpdateSourceTrigger.Explicit` are written when the edit is committed and
  never when it is cancelled. A template column's editor that binds the row itself - a field writing a debugged value
  into a running game, say - can now be taken back with Escape; bound `LostFocus`, its leaving wrote it even on a
  cancel.
- A template column's editor that is a text field opens with its text selected, as a text column's does: what is
  typed replaces the value instead of going in front of it.
