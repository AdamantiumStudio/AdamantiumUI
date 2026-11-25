### Added

- Tests of a `WebView` given a page another one opened (`NewPage`): it takes up Chromium's own page, sizes it and runs it
  through its bridge, the page outlives its opener, and one of the menu is made at its address.
