### Added

- A tool bar down the side is a column as wide as a command. Each command of a `ToolBar` or a `ToolBarGroup` stands in a
  `ToolBarItem`, which shows it in the form its bar decides: on the column, as it is while it fits
  (`ToolBar.ColumnWidth`, a theme metric) and folded into a `ToolBarFoldButton` when it does not - the button opens the
  command itself, moved, not copied, on a card beside the column under its label. `ToolBar.VerticalForm` sets it per
  command: `Auto`, `Fold`, `Keep` or `Hide`; `ToolBar.FoldedIcon` gives the folded button its icon, else it shows the
  first two letters of the label. A label tied to a command by `AutomationProperties.LabeledBy` leaves the column with
  it, for the command's tooltip and the card's header. Along a bar and in its overflow menu every command stands as
  before.
- `ToolBar.CommandOrientation`, inherited: vertical on the column, horizontal along a bar, in the overflow menu and on a
  card. The themes draw the narrow forms from it: a `NumericUpDown` stands vertically, a `DropDownButton`'s arrow and a
  `SplitButton`'s arrow part go under the content, and their lists open beside the column.
- `NumericUpDown.Orientation`: vertical, the box is as narrow as its number, a `Split` pair over and under it.
  `NumericButtonsPlacement.Top` and `Bottom` put both buttons over or under the number, each half its width, along or
  down; `AreButtonsSwapped` swaps them in every placement.
- `NumericUpDown.Unit`: what the number counts - cm, °, pt - shown after it along, under it down; neither typed nor read
  back.

### Changed

- A `ToolBarTray` moves no bar itself any more: its grip is a `DragHandle`, and an application lets the user carry bars
  with `DragDrop` - its ghost, its drop - by putting a `ToolBarDragBehavior` on the tray, or on a control that holds
  trays of its own, without reaching into its template. The tray says where a bar would land and puts it there
  (`ShowDropPlace`, `HideDropPlace`, `DropBar`).
- The room between bars and between rows is the tray's (`ToolBarTray.BarSpacing`, `BandSpacing`, theme metrics), only
  between neighbours; bars have no margin of their own, so a bar alone down the side stands in the middle of its tray.
- A contextual bar down the side shows its `ContextualHeader` under its grip, turned to read top to bottom, as a folded
  pane's tab reads; it showed only its color there before.

### Fixed

- The chip a context's name sits on - a ribbon's contextual group, a contextual tool bar's plate - was dark in the light
  variants too, a dark hole in a light window; it is light there now.
- A `Grid` child spanning several columns or rows counted for nothing when none of them took a size of its own - Auto
  tracks, or stars measured with no limit - and only in part when some did: the excess was shared among every track
  left, the Auto ones too, which take none of it. A child across Auto tracks alone now sizes them, and the excess goes
  whole to the tracks that take it.
- A `{ResourceReference}` written before there was an application - a template built ahead of it - threw when its
  element joined the visual tree, or never resolved: it looked in the resources there were when it was written. It looks
  among the application's resources as they are when the element arrives.
