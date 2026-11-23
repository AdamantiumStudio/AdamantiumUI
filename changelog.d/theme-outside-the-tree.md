### Fixed

- A component outside the tree reading the theme - a drawing of an icon - follows it wherever it changes: it answers
  to the element it hangs under (the `Image` showing it) and that element's `ThemeContext` scope, moves with a whole
  theme swapped and with a scope following the system to its other variant, and keeps no theme swapped out alive.
- A trigger's `{ThemeResource}` on a named part outside the tree (a drawing, an aura) was handed to the type converter
  as it stood: it connects to the theme as on an element.
- A theme swapped in while a swap still waited for its frame took the waiting one's place as the theme left: the
  theme before stayed with its palette live and its readers on it. The swap waiting keeps the theme it started from.
- `{ObservableResource}` on a component outside the tree kept it alive for good: the resource manager, which lives as
  long as the application, held its listener. It holds every listener weakly now.
