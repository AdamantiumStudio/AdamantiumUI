### Changed

- Graphite's `ScrollViewer` bars, which are always on, no longer lie over the content: while a bar shows it takes a
  strip of its own beside the content, so a list row's right edge or a line's last word is never under it. Fluent's
  and macOS's bars, which fade out, still lie over the content, as Graphite's infinite canvas bars do.
- `TreeDataGrid` lays its columns out against the width its rows are seen in, and ends the header, new-record and
  totals strips there: under a theme whose vertical bar stands beside the rows, a star column fills up to the bar and
  the header stands over its own cells only. Graphite's grid has no blank after the last row (`EndPadding` 0), as its
  horizontal bar no longer covers it.
