### Added

- `FileDialog.OpenMany`: the Open dialog letting several files be chosen at once - a playlist, a batch to import -
  answering their paths, or none when canceled; on Windows (the shell's dialog), macOS (`NSOpenPanel`) and Linux (the
  FileChooser portal's `multiple`). `IFileDialogPlatform.OpenMany` answers with the one file of `Open` for a platform
  of an application's own that does not say otherwise.
