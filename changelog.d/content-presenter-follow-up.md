### Changed

- `DropDown.DisplayContentTemplate` is read-only and computed, as WPF's `SelectionBoxItemTemplate`: the item template
  for a chosen item, none for the placeholder. `DisplayContentTemplateSelector` is gone: the header's presenter takes
  `ItemTemplateSelector` itself, in the tree. `DisplayContent` is read-only, as its documentation said.

### Fixed

- Sandbox: the diagnostics window showed an empty "Presentation" chooser in a window with no presentation of its own.
- Sandbox: the web page built its permission question and its opened-page panel, a browser included, with nothing to
  show. Both are chosen by data type now, so a closed page lets its browser go.
