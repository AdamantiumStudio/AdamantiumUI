### Fixed

- A universe hosted in a panel no longer fails its frame with "Control is not attached to visual tree" when the panel's
  tab is switched away while the universe reads where the pointer is on it. The point is found through where the
  surface stood when the UI's own thread last placed it - after each layout pass and overlay arranged, each pointer
  move over it, a render transform given to it or above it, and as it comes into the tree - instead of a walk of the
  tree from the universe's thread; taken out of the tree, it answers by where it last stood.
