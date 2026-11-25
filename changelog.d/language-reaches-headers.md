### Fixed

- Text bound to a property of any content - a `Header`, a button's `Content` - is read again when the application's
  language changes, as text bound to a string property is.
- The caption of a docking panel follows the header of the pane it names; before, it kept the one the pane had when it
  was selected.
- A `NumericUpDown` writes its number the new language's way when the language changes; a number half typed is left.
- A `DropDown` without an item template asks its items for their words again when the language changes.
