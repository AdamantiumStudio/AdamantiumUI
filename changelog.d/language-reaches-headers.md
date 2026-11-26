### Fixed

- Text bound to a property of any content - a `Header`, a button's `Content` - is read again when the application's
  language changes, as text bound to a string property is.
- The caption of a docking panel follows the header of the pane it names; before, it kept the one the pane had when it
  was selected.
- A `NumericUpDown` writes its number the new language's way when the language changes; a number half typed is left.
- Content shown by its `ToString`, no template given - a drop-down's choices, a list's rows - is asked for its words
  again when the language changes; an editable `DropDown` writes its choice again.
- A `Clock`, a `DateTimeField`, a `DurationField` and an open date picker write themselves again when the application's
  language changes; before, only a `Language` set on them or an owner did that.

### Added

- `UIComponent.OnLanguageChanged`: told when the language an element's text is in changes - its own `Language`, an
  owner's, or the application's, by its window on the window's thread. The controls that write words, numbers or dates
  of their own are told this way instead of each listening to the application's language.
- `WindowCommand.Label` and `ToolTip` are virtual: a command can say them in the language of the moment.
