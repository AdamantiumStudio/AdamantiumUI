### Fixed

- A theme's palette entry set in the theme's scope (`IResourceManager.SetResourceInScope`, `ResourceScope.Theme`) takes
  effect: it was looked for only in the theme's dictionaries, where a palette entry is not, and nothing changed - the
  sandbox's "Cycle critical color" did nothing. The new brush takes the entry's place; what read the entry once keeps
  the old one.
