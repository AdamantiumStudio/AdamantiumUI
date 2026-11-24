### Fixed

- `WebView` on Linux starts with no X server. Chromium, though it draws off screen, starts its X11 platform by
  default, and with no `DISPLAY` CEF did not start at all ("Missing X server"). Now, with no X server, it starts on the
  Wayland compositor (`WAYLAND_DISPLAY`), and with no display at all, with none (`headless`, where a site asking for the
  user's location is not asked). With an X server nothing changes.
