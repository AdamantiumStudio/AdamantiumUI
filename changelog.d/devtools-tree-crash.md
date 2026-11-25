### Fixed

- A `WebView` whose page closed while its accessibility tree was on could bring the application down as it quit, or
  leave pages that never closed or opened. Chromium asks for the page's accessibility handler with each change of the
  tree, a closing page's too, and got one already let go of. The handler now lives as long as Chromium holds the
  browser's render handler.

### Changed

- The DevTools pane shows its page's tree to automation and screen readers, as other views do, instead of a pane with
  nothing in it.
