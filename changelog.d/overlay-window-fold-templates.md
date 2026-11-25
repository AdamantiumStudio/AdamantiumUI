### Added

- `OverlayWindow.MinimizeTemplate` and `MaximizeTemplate`: what its collapse button shows, the content shown and
  collapsed - any icon, not only the theme's chevron. Each theme gives its own glyph as the default; with no template
  the button shows nothing.

### Changed

- A `ContentPresenter` with a `ContentTemplate` and no content builds the template, as WPF does; the built visual's
  data context is null. Before, no content showed nothing whatever the template. Content cleared under the same
  template keeps the visual and only its data context goes.
- Controls that show a chosen item's template now give none when nothing is chosen: `TabControl`
  (`SelectedContentTemplate`), `PaneGroup` (new `DockedContentTemplate` and `FlyoutContentTemplate`, the empty host's
  is null) and `DropDown` (new read-only `DisplayContentTemplate`, as WPF's `SelectionBoxItemTemplate`: the item
  template for a chosen item, none for the placeholder). A `PropertyRow` with no
  editor to show sets no template, and a tab overflow row of a tab with no icon has no icon template.
