### Fixed

- A list on a page kept between visits (`x:KeepAlive`) did not scroll any more once the page was come back to: its
  scroll viewer had let go of the panel it scrolls for and did not find it again.
- A `Selector` - a `ListBox`, a `DropDown`, a `TabControl` - whose bound selection was refused, or answered with another,
  while it was being written kept the refused item selected; a later change of the items selected it again.
- `TabControl.MaxOpenedTabs` closed tabs of a bound list in the middle of the list's own change notification: another
  listener of the list heard of the closing before the adding, and threw. The cap now acts once the list has told
  everyone, before the next frame.
- A tab hidden by a `CollectionView`'s filter is found by `SelectedValue`, and one that is its own page is no longer
  discarded when another tab is chosen.
- The sandbox gallery's filter follows the language.
