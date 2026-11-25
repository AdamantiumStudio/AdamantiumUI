### Added

- Gestures on a trackpad, on macOS: a pinch and a rotation are `Gestures.ManipulationEvent` of the element under the
  pointer that takes gestures (`IsManipulationEnabled`) - a pinch over any other turns the wheel with Control, as on
  Wayland. Two fingers moving scroll, and pan an element that asks for it (`Gestures.IsTrackpadPanEnabled`), as the view
  of a universe does: they turn its camera, and with Option orbit it. With Command they zoom, as the wheel does.
  `ITrackpadPanTarget` lets an element say where over it they do; an `InfiniteCanvas` whose wheel zooms is moved by them.
- `ManipulationEventArgs.IsTrackpad`.
- macOS: a view's mouse-look (`SetRelativeMouseMode`) - the cursor hidden and still, the mouse's own movement reported.

### Changed

- macOS: a key is the one the keyboard layout names, as on Windows: on a French layout Command+A is where A is. A layout
  without Latin letters takes them from the Latin one beside it; `PhysicalKey` stays the position.
