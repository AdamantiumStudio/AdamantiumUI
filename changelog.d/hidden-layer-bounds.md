### Fixed

- Hidden windows on macOS (`ADAM_HIDDEN_WINDOWS=1`) drew a still page at the display's rate: their layer had no size of
  its own, and the driver called every present suboptimal. A window on screen was not affected.
