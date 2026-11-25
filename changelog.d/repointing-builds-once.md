### Changed

- An `ItemsProperty` gives each listed item its own copies of its lines (`PropertyDefinition.CopyFor`), pointed at that
  item for as long as it is listed, with bindings of their own; a copy names the line it was made from
  (`PropertyDefinition.Original`, `PropertyDefinition.AsWritten`). One line used to stand for every item, pointed at
  each in turn and left at the last. A kind of line has to be made without arguments to be listed.
- An edit through an item's line is reported (`PropertyValuesChangedEventArgs.Property`) as the line as written - one
  line however many items, still standing when the item is gone - and `PropertyValuesChangedEventArgs.Line` is the row's
  own copy. `PropertyGrid.Refresh` given the line as written reads every copy's row again.

### Fixed

- A list's line read off each item - `IsVisible="{Self Inspected.Shown}"`, a nested list's `ItemAction` - no longer
  hangs the grid when the items answer differently, and each item's line follows its own item afterwards: what it shows,
  its words, read-only state and button, at any depth of path. A write through one item's line leaves the others' lines
  as they were.
- An item's heading follows the item - renamed, it says the new name with nothing built again; with no name, its place,
  counted again as the list changes - and may be a `MultiBinding`. Its button takes the list's `ItemAction`,
  `ItemActionIcon` and `ItemActionTip` replaced later, the items' lines kept.
- An object listed twice, or two equal values, is two groups of lines; a list of values keeps its lines pass after pass.
- A list's line set later reaches every item's copy at once; given or taken a binding, at once where what it says
  changes and with the next build otherwise; given other lines, the copies are made again. A change reaching every copy
  builds the grid once.
- An item taken out of a list, a list folded or no longer shown, and a grid let go of release the lines made for their
  items, and the items with them.
- An item's rows keep their targets pass after pass: built again, they are refreshed, not bound anew, so a spinner held
  on one of them stays one move.
- A line read again while the grid builds - a binding found late, or a value handed out new on every read - runs the
  pass once more at most for each line as written and each making of its copies, instead of without end.
