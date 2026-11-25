### Added

- `WebViewOptions.Components`: the components Chromium may fetch from Google and keep up to date - Widevine (the video
  and music sites protect), the hyphenation dictionaries, the list of revoked certificates. None by default.
- `WebViewEngine.GetComponentStatus` and `WebViewEngine.InstallComponentAsync`: whether a component is installed and its
  version; fetching it now rather than when Chromium gets to it, about a minute after it starts.

### Changed

- Chromium no longer fetches anything from Google that the application did not ask for. It used to fetch some twenty
  components unasked, Widevine and Chrome's machine learning models among them; the others are now never fetched. Our CEF
  build `cef-156.0.26-2` lets only the components named on its new `--allowed-components` switch be registered, and does
  not load a copy an earlier run left of one it leaves out.
