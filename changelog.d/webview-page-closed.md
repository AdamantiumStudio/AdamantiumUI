### Added

- `WebViewBridge.PageClosed`: a page closed itself with `window.close()`, where Chromium allows it (a page a script
  opened, or one never navigated away from). CEF closes such a browser itself; the `WebView` used to keep showing the dead
  page without telling anyone. Now it raises the event and stays empty until it is given an address or a page again.
