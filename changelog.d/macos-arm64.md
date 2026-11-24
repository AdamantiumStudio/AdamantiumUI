### Added

- Windows on macOS answer their caption's minimize, maximize and restore buttons, and follow `ResizeMode`, the minimum
  and maximum size, `Topmost`, `WindowOpacity`, `TransparentToInput` and `Owner`.

### Changed

- An application that states no `StartupTheme` opens on the MacOs theme on macOS; elsewhere on Fluent, as before.
- The default UI font on macOS is San Francisco, the system's own, instead of Helvetica.
- On macOS the caption buttons stand on the left in every theme, unless the application sets `CaptionButtonPlacement`.

### Fixed

- The MacOs theme's traffic lights were smaller, closer together and further from the window's edge than
  macOS's, and the zoom light's wedges stood too far apart.
