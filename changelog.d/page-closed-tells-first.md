### Changed

- `WebViewBridge.PageClosed` is raised while the view's `Source` and `Title` are still the page's; the view lets go of
  them after. A browser keeps the tab of a page that closed itself to bring back by them.
