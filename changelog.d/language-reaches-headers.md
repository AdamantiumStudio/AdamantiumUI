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

- A new application language reaches the windows as a theme swap does: every window's `UpdateLanguage()` has what is in
  it read the language on the next layout pass, through `OnLanguageChanged` - a virtual of every element, also told
  when its own `Language` or an owner's changes. An element out of every window at the time reads it on the next pass
  once back in one. The controls that write words, numbers or dates of their own are told this way instead of each
  listening to the application's language.
- `WindowCommand.Label` and `ToolTip` are virtual: a command can say them in the language of the moment.

### Changed

- `HeadlessApplication` lists its windows, and a window an in-process `AutomationSession` hosts is listed in the current
  application until the session is disposed: a theme swap and a new language reach headless windows as they reach
  shown ones.
