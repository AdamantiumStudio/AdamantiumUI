### Fixed

- A binding to the source only (`OneWayToSource`) on a text property no longer follows the application's language: it
  never writes its target, and on a read-only property the switch threw.
