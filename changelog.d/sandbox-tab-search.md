### Fixed

- A `TabControl` bound through a `CollectionView` closes, moves, tears off and takes back its tabs in the list the view
  looks at - closing or moving one threw. A tab the view's filter hides is still open: its kept page is no longer let go
  of, it counts and keeps its age under `MaxOpenedTabs`, and the selected one stays selected, its page shown, with no
  index until the filter lets it back into the strip.

### Changed

- The sandbox gallery's tabs stand on the left, and a field over them filters the strip as it is typed into.
