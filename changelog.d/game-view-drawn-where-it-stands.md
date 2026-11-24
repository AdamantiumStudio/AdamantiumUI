### Added

- `IRemoteUniverse.DrawAtBreakpoint`: asks the host for the world drawn as it is, with no step of the game - at a
  stop of a game a debugger holds, for its view to show the game there, at its size, a view made anew too. Held at a
  breakpoint or a step, what the game moved so far is drawn where it is; paused anywhere, as the last frame placed it.
  `IRemoteUniverse.GoOnAsync`, asked before the game is let go on: done once the host draws so no more.
