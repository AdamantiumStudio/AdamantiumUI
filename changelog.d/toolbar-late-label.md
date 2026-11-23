### Fixed

- A label tied to a tool bar's command by a binding that resolves late - `LabeledBy="{Binding ElementName=...}"` on a
  bar first shown down the side - stood on the column as a command of its own, and folded when wide; the commands are
  placed again when a command's label changes, so it leaves the column with its command.
