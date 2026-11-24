### Changed

- `WebViewBridge.PageClosed` is an `EventHandler<WebPageClosedEventArgs>`: where the page was (`Address`, null for one
  that closed itself before it was shown) and what it was called (`Title`), taken before the view lets go of them. A
  browser keeps the tab of a page that closed itself to bring back by them. Still raised last, so an address the
  application gives the view then stands.
