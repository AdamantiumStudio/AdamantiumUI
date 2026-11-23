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

- A contextual bar down the side shows its `ContextualHeader` under its grip, turned to read top to bottom, as a folded
  pane's tab reads; it showed only its color there before.

### Fixed

- A `{ResourceReference}` written in a template threw when the element joined the visual tree with no application
  context to look it up in; it is left unresolved there, as the lookup before it already was.
