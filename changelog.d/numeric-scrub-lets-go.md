### Fixed

- A `NumericUpDown` dragged sideways (`IsDragScrubEnabled`) dropped its drag as it began and then kept the pointer when
  let go, so no other control under the pointer could be pressed or dragged until it was clicked again - the second
  number of a property grid did not scrub. Its text's selection, let go as the drag began, gave up the pointer, and the
  text box's capture taken over was told as the control's own (the event's source set to the control as it left the
  template); both were taken for the drag's end. The selection is let go before the drag begins, and only a capture
  that went to something else ends it.
