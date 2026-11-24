### Added

- macOS on Apple Silicon: the sandbox opens, draws through KosmicKrisp and takes the mouse and the keyboard. AppKit is
  called from C# through the Objective-C runtime (the engine's `Adamantium.MacOS`); the application shows its icon in
  the Dock.
- Windows on macOS: the caption of the theme instead of the system's, minimizing, maximizing, the full screen (⌃⌘F),
  modal windows, `ResizeMode`, the minimum and maximum size, `Topmost`, `WindowOpacity`, `TransparentToInput`,
  `Owner`; `Displays` tells the monitors, so windows open centered and come back where they were closed.
- The splash window on macOS: drawn throughout the start, its mouse answered between the start's steps - AppKit lives
  on the application's own thread.
- `OverlayWindow.CaptionButtonPlacement`: which side of the caption the buttons sit on, in Fluent and Graphite.
- A splash window's close button stands on the side of `CaptionButtonPlacement`: on the left on macOS.
- The application template's theme may be the platform's, which is its default.

### Changed

- An application that states no `StartupTheme` opens on the MacOs theme on macOS; elsewhere on Fluent, as before.
- The default UI font on macOS is San Francisco, the system's own, instead of Helvetica.
- On macOS the caption buttons stand on the left in every theme, unless the application sets `CaptionButtonPlacement`.
- Takes Adamantium.Vulkan.Slang 1.2.0 and ShaderObjectLayer 1.0.3.

### Fixed

- The frame verifier raised an alarm over text recolored in place between a frame's record and its drawing, as switching
  the theme's variant does.
- The MacOs theme's traffic lights were smaller, closer together and further from the window's edge than macOS's, and
  their glyphs thinner.
- Off-screen rendering asked the Vulkan loader, not the instance, for a headless surface: where the loader lists
  another driver's extension - macOS with MoltenVK installed - every off-screen frame failed.
