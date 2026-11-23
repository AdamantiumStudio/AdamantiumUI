### Added

- `IWordPredictor` on Linux: the words a beginning goes on to from the hunspell dictionaries installed (the ones Enchant
  checks spelling with: `DICPATH`, `~/.local/share/hunspell`, `/usr/share/hunspell`, `/usr/share/myspell`), read as
  hunspell(5) describes the .aff and .dic files - encodings, flag kinds and aliases, prefixes, suffixes, cross products,
  a suffix's own suffixes; what a misspelled word was meant as from Enchant. Only the stems a beginning can grow from
  are expanded, when asked, never a whole language. The dictionaries have no frequencies: shorter words come first.
  There is no next word.
