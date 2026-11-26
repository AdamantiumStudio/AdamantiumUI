### Added

- `InfoBar`: a message across a page - what happened (`Title`), why (`Message`) and the actions it holds as its content,
  under the text. Its `Severity` - informational, success, warning, error - gives it its own ground and icon in every
  theme, the icon's shape telling it apart without the colour too. Closed by its close button while `IsClosable`, or by
  `IsOpen` (two-way); `Closed` says so; closed, it takes no room, nor does a message or actions it has none of. To
  automation it is a group named by its title.
- Palette colours `SystemFillColorAttention` and the grounds of messages - `SystemFillColorAttentionBackground`,
  `SystemFillColorSuccessBackground`, `SystemFillColorCautionBackground`, `SystemFillColorCriticalBackground` - in every
  theme's light and dark palettes.
- The sandbox has an InfoBar page.
