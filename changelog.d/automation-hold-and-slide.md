### Added

- Automation: a hold that slides on. `AutomationElement.HoldAndSlideToAsync(destination)` (and
  `PenHoldAndSlideToAsync`) holds a finger - or a pen - still past the holding time, then draws it, still down, to the
  middle of another element and lifts it there. The destination is found once the hold has started, so it may be one
  the hold opened: a key's alternates, a list over a key. In scenarios `SlideTo="<selector>"` on a `Touch` or `Pen`
  step with `Gesture="Hold"`; in adam-auto `touch <selector> hold --to <selector>`. Simulated input only.
