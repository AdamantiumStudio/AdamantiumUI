### Added

- `PlatformSettings.UserLanguages` (and `INativePlatformSettings.UserLanguages`): the languages the user set up in the
  OS, in the user's order, as language tags. Windows' language list, most wanted first, read again only after the
  user's list changes (about a microsecond an ask). On Linux, the languages of the desktop's input sources, the
  session's own language first and the rest in their switching order (GNOME, through the Settings portal, read apart
  from the appearance and followed as they change), else the session's `LANGUAGE` or locale as gettext takes them. None while no platform says.

### Changed

- The Windows word predictor reads the user's languages through the same reader.
