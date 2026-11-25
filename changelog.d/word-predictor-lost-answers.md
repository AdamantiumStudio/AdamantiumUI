### Fixed

- Word suggestions on Windows went missing now and then: Windows' input service fails an ask once in a while, and that was
  taken for no words. A failed ask is asked once more; failed again, it is logged.
