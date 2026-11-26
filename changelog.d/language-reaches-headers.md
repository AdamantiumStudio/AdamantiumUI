### Fixed

- Text bound to a property of any content - a `Header`, a button's `Content` - is read again when the application's
  language changes, as text bound to a string property is.
- The caption of a docking panel follows the header of the pane it names; before, it kept the one the pane had when it
  was selected.
- A `NumericUpDown` writes its number the new language's way when the language changes; a number half typed is left.
- Content shown by its `ToString`, no template given - a drop-down's choices, a list's rows - is asked for its words
  again when the language changes; an editable `DropDown` writes its choice again. A number or a date shown that way is
  written in the application's language, as the drop-down's box writes it, not the system's.
- A `Clock`, a `DateTimeField`, a `DurationField` and the date pickers write themselves again when the application's
  language changes; before, only a `Language` set on them or an owner did that.
- A docking panel taken out of the tree stops following its pane's header, so a pane that outlives the panel no longer
  keeps it alive.

### Added

- A new application language reaches the windows as a theme swap does: every window's `UpdateLanguage()` has what is in
  it read the language on the next layout pass, through `OnLanguageChanged` - a virtual of every element, also told
  when its own `Language` or an owner's changes. An element out of every window at the time reads it on the next pass
  once back in one, and a window made before a change reads it once the application lists it. A language set on
  another thread is taken to the windows on the loop thread. The controls that write words, numbers or dates of their
  own are told this way instead of each listening to the application's language.
- `LayoutManager.QueuedCounts()` and the "layout still to do" note show the language queue.
- `WindowCommand.Label` and `ToolTip` are virtual: a command can say them in the language of the moment.

### Changed

- `HeadlessApplication` lists its windows, and a window an in-process `AutomationSession` hosts is listed in the current
  application until the session is disposed: a theme swap and a new language reach headless windows as they reach
  shown ones. Disposing the session closes its windows still open.
