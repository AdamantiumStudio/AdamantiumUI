### Added

- Groups in an `ItemsControl`, as in WPF: a `GroupStyle` in `ItemsControl.GroupStyle` (one per level, its
  `HeaderTemplate` given the `CollectionViewGroup`) shows the groups of a grouped `CollectionView` source. The items
  stay one list - their indices, the selection and the keyboard count no header - and each group's header (`GroupHeader`,
  made by `ItemsControl.GroupHeaderGenerator` at the places of `ItemsControl.GroupHeaderPlaces`) is a row of the panel
  before the group's items: a line across a `WrapPanel`, a row of a `StackPanel`, virtualized or not. Only the headers
  of the lines in view are made, as only those items are; the arrows pass a header by. Each level's headers are as tall
  as their own template, and a grouped `StackPanel` keeps its rows of another height (`ItemExtentExceptions`).
  `GroupStyle.CanCollapse` lets a click on a header fold its group to the header and open it again
  (`GroupHeader.IsExpanded`, kept by the group's name across changes of the items; automation's ExpandCollapse); a
  folded group's items are not made, even with the window across them. The themes draw the header and its arrow.
  Automation reads the list as it shows: each header before its group's items, a folded group by its header alone.
  `RibbonGallery` groups its items through this view instead of an order of its own.
- A grouped `CollectionView` goes group by group in its own items, the ordering kept inside each, so a list shows a
  header and then the group's items; an item joining or leaving its group is one Add or Remove there, and a Reset only
  when it changes the groups' order (or with more than one level).
- Drag and drop in a list showing groups. A drop slot is now a `DropSlot` - the index, and the group it lands in with
  that group's items (`GroupStart`, `GroupEnd`) - since one group's end and the next one's start are the same index:
  `IDropGapPanel.TryGetDropSlot` and `ShowDropGap` take it, `VirtualizingPanel.DropGapGroup` holds the gap's group, and
  `DragDropEventArgs.DropGroup` tells the target, with `InsertBefore` an item of that group or null for its end (putting
  the item in another group is the target's to do). The gap opens as a cell of its group in a `WrapPanel` or a
  virtualizing `StackPanel` - the items after it in the group move along, a full last line makes the group a line
  longer - and the caret, where it is the cue, stays within the group; a stack that does not virtualize tells the group
  too. On a header a drop goes to the start of an open group, to the end of a folded one, framed on its header; a drag
  resting on a folded header opens it (`GroupHeader` is `ISpringLoadable`).

### Changed

- Breaking: `IDropGapPanel.TryGetDropSlot` gives a `DropSlot` instead of an index, `ShowDropGap` takes one
  (`DropSlot.None` closes the gap), and so does `VirtualizingPanel.TryGetDropSlot`; `TiledPanel` keeps its index-based
  methods.

### Fixed

- Text on something dragged (`DragDrop.AllowDrag`) selected under the press that picked it up - a letter of a tile
  highlighted, a double click selecting its word and taking the keyboard: a press a drag source takes is marked
  (`PointerPressedEventArgs.IsTakenForDrag`), and text under it neither selects nor takes the focus.
- A `ContentControl` without a template of its own showed data content as large text, passing its `ContentTemplate`
  (and `ContentTemplateSelector`) by: such content is now shown through the template.
