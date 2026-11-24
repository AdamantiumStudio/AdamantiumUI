### Fixed

- A `PropertyGrid` line shared by a list's items and read off each - `IsVisible="{Self Inspected.Shown}"`, or a nested
  `ItemsProperty` whose `ItemAction` is bound to its item - hung the grid when the items answered differently: pointing
  the line at each item asked for another build, every pass. What a line says as it is pointed at its item - its
  layout and its words - is read by that same pass and asks for nothing more.
- ...and once built, such a line heard only the item it was pointed at last: another item changing its answer later -
  `Shown` turned off on the first - left its line as it was. The grid now hears each listed item for the first property
  of each path its lines read off it (`Shown` of `Inspected.Shown`), of its heading (`ItemHeader`) and of a list inside
  it; nothing else of the item builds anything. A renamed item's heading says its new name.
- What a listed item changes is built on the loop, once for however many changed before it: never inside the write
  that changed it, never on the thread of a game that changed it.
- An item's lines are given the same targets pass after pass: built again, they are refreshed, not bound anew, so a
  spinner held on one of them stays one move.
- An `ItemsProperty` whose `ItemAction` was replaced by another command - the panel's view-model replaced - kept the old
  one on its items' buttons. What the grid itself reads again while it builds - a binding found late - runs the pass
  once more, once per line: a view-model handing out a new command, or new choices, on every read no longer builds it
  without end.
