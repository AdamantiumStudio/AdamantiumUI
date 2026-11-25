### Added

- macOS: the clipboard - text, HTML, RTF, pictures in their own encoding, files and formats of one's own.
- macOS: the menu bar's menus - the application's (About, Services, Hide, Quit) and Window - in the application's
  language.
- macOS: the open, save and folder dialogs (`FileDialog`).
- macOS: spelling and word completion (`ISpellChecker`, `IWordPredictor`) by the system's dictionaries.
- macOS: icons in the menu bar (`NotificationArea`), starting at sign-in (`ISystemStartup`), gamepads with
  vibration, of the triggers too where the gamepad gives it.
- macOS: the system's light or dark appearance (`ThemeVariant.System`), the user's languages, the times of a double click
  and a repeated key, how far a press drags, the live state of the mouse and keyboard.
- macOS: `IApplicationPlatform.WindowFromScreenPoint`.

A hidden run (`ADAM_HIDDEN_WINDOWS=1`) keeps from what is the user's: their clipboard, cursor, keys, gamepads and screen.

### Changed

- macOS: the Trash is asked directly, not through the Finder, for which the system asked the user's permission.

### Fixed

- Curve fills: no covered pixels outside a corner whose edge runs through pixel centres.
- Sandbox: the multitouch page's ten columns fit the page beside the tabs on the left.
