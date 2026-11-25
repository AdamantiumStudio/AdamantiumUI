### Fixed

- A view built off the loop thread (`DeferContent`) whose controls build some of their parts anew before it is shown -
  calendars, date and time fields, clocks - was kept whole by the theme's brushes after it was left.
- Every closed window left its graphics device object behind, some 20-30 KB of managed memory each.
