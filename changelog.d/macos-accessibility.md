### Added

- macOS: the Mac's accessibility - VoiceOver, Zoom, Voice Control - sees the elements automation knows, as
  `NSAccessibility` elements: their roles, names, help, identifiers, places and state, the one under a point and the one
  with the keyboard when asked; a press invokes, toggles, selects or opens, a range is read, stepped and set, a value
  set, an expander opened and shut, a context menu shown - only what the element can do is offered. Made only when the
  system asks; what changes is not told yet (focus, values), nor is text read by range.

### Changed

- Linux: the accessibility bridge waits for the loop through the same call the Mac's does, and a failure there keeps
  its stack.
