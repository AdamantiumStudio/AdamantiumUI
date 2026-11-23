### Added

- `INativeKeyboard.ForegroundWindowId`: which window the system gives keys to, the same number while it is the same
  window whatever its title says (an editor marking its document unsaved stays the window it was); 0 where the system
  does not say. Windows: the foreground window's handle. An on-screen keyboard tells by it that typing went to another
  window.
