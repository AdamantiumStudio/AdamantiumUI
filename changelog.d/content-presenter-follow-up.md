### Changed

- `DropDown.DisplayContent` is read-only, as its documentation said.

### Fixed

- Sandbox: the diagnostics window showed an empty "Presentation" chooser in a window with no presentation of its own.
- Sandbox: the web page built its permission question and its opened-page panel, a browser included, with nothing to
  show. These parts, and the diagnostics chooser, are chosen by a template selector now: it is not asked for no
  content, so nothing is built, and a closed page lets its browser go.
