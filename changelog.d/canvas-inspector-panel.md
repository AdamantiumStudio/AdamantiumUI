### Added

- `InfiniteCanvas.InspectorPanelTemplate`: the whole inspector panel - open and folded - replaced by an application's own.
- `IsInspectorOpen`, `InspectorWidth`, `MinInspectorWidth`, `FoldInspectorCommand`, `HidesFoldedInspector` and
  `ShowsDrawingActions` of an `InfiniteCanvas`; whether the
  panel is open and how wide is kept in the canvas's `Layout`.

### Changed

- The inspector of an `InfiniteCanvas`, a `DrawingBoard` and a `NodeGraph` is a panel beside the plane, on the right, no
  longer floating over it: pulled by a splitter, folded to a strip, as tall as the canvas. A template that wants the
  inspector names `PART_Viewport`, `PART_Inspector` and `PART_InspectorSplitter`; `PART_Inspector` is not a `CanvasPane`
  any more, and a saved layout's place for it is ignored.
- `InfiniteCanvas.ShowsInspector` binds two way by default.
- A `CanvasInspector` standing in a canvas and given none finds it by itself.
