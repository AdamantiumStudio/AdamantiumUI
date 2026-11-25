### Changed

- The language server's type models share the metadata they read (`MetadataReferenceCache.Shared`): each model kept a copy
  of every assembly it was built from outside the managed heap.

### Fixed

- A change of the application's language wrote into bindings of elements already collected, which could fail on what they
  held.
