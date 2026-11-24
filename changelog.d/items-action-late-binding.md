### Fixed

- An `ItemsProperty` whose `ItemAction` was given after its lines were built - bound, its command found only once the
  grid had a data context - showed no button beside its items until the list was built again for something else. Its
  lines are built again when the button comes or goes.
