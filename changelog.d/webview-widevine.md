### Added

- `WebViewOptions.Components`: the components Chromium may fetch from Google and keep up to date - the hyphenation
  dictionaries and the list of revoked certificates (CRLSet) by default, Widevine (the video and music sites protect)
  when the application adds it. `None` fetches nothing.
- `WebViewEngine.GetComponentStatus` and `WebViewEngine.InstallComponentAsync`: whether a component is installed and its
  version; fetching it now rather than when Chromium gets to it, about a minute after it starts.

### Changed

- Chromium no longer fetches components the application did not ask for. It used to fetch some twenty unasked,
  Widevine and Chrome's machine learning models among them; now it fetches the hyphenation dictionaries and the list of
  revoked certificates, and Widevine only when named - the others never. Our CEF build `cef-156.0.26-2` lets only the
  components named on its new `--allowed-components` switch be registered, and does not load a copy an earlier run left
  of one it leaves out.
- Chromium's background work - a component's download, among others - no longer waits for a page: with none up it used
  to start three minutes on, now in about five seconds (Chromium's `ImprovedStartupBestEffortDelay`).
- Protected video and music no longer play until the application adds `WebViewComponents.Widevine`.
