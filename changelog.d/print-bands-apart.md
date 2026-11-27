### Changed

- An element printed with a shadow or a glow stays vector, its own text too: only its shadows and glows are pictures,
  the outer ones laid under it, the inner ones over its fills and under its text, each made alone by the GPU.
- `IVisualRenderer.RequestSnapshot(visual, region, scale, content, onReady)` takes a `SnapshotContent` in place of
  `bool alone`: an element and all it holds, its own drawing, its outer or inner shadows and glows alone, or its text's
  glyph effects alone.

### Fixed

- A text's own effects - `TextShadow`, `TextGlow`, `TextOutline`, `TextReflection` - are printed. They were left out of
  a PDF or a print; now they are a picture of the effects alone, laid over the element's fills and under its glyphs,
  which stay vector and selectable. The print preview, which draws them with the text, gets no second copy.
- A text's own background is printed, under the text, as on the screen; the PDF and XPS writers left it out.

### Added

- `IDrawingSession.DrawsGlyphEffects`: whether text drawn into a session shows its glyph effects itself, as the screen
  does; false for a vector writer.
