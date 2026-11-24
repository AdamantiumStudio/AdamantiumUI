### Added

- `WebView`: the page's insides to automation and screen readers. Its peer holds the page's document, read from
  Chromium's accessibility tree - links, buttons, fields with their values, check boxes, lists, headings, text - with
  their places on screen, shown as Chrome shows them. The tree is switched on only once automation asks for the view's
  children: it costs Chromium work on every change of the page.
- What is done to a page's node is done as a user does it, through the page's own input - a click in its middle, keys
  typed, the wheel turned - so a page sees trusted events (`isTrusted`), never a script acting on it.
- Focus, value, check and structure changes of the page's nodes are told to automation listeners (UIA on Windows, AT-SPI
  on Linux).
- `AutomationEvents.Raise` and `AutomationEvents.RaisePropertyChanged` for a part shown inside an element that is no
  element of its own, such as a node of a web page.
- Sandbox: the Web page's address takes `data:` addresses, as other browsers do.
