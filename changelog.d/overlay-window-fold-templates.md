### Added

- `OverlayWindow.MinimizeTemplate` and `MaximizeTemplate`: what its collapse button shows, the content shown and
  collapsed - any icon, not only the theme's chevron. Each theme gives its own glyph as the default; with no template
  the button shows nothing.

### Changed

- A `ContentPresenter` with a `ContentTemplate` and no content builds the template, as WPF does; the built visual's
  data context is null. Before, no content showed nothing whatever the template.
- Controls that show a chosen item's template now give none when nothing is chosen: `TabControl`
  (`SelectedContentTemplate`), `PaneGroup` (new `DockedContentTemplate` and `FlyoutContentTemplate`, the empty host's
  is null) and `DropDown` (new `DisplayContentTemplate` and `DisplayContentTemplateSelector`). A `PropertyRow` with no
  editor to show sets no template.
