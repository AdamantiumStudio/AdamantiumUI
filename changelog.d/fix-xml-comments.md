### Fixed

- Doc comments that did not hold: a reference to a member renamed or gone, an overload left unnamed, a parameter
  missing or one that is not there, a comment above the wrong member. The CEF bindings' comments are escaped, so a
  `<ping>` or a `1 << 3` in CEF's own text no longer breaks them.
