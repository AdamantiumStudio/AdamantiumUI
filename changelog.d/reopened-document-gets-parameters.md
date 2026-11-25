### Changed

- `DockingRegion.OpenDocumentAsync`: a document open already is navigated to again with the new parameters before it is
  brought to the front, so it hears what it is asked to show now. Its `OnNavigatedToAsync` runs once per opening.
