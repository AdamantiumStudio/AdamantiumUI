### Fixed

- `{ThemeResource}` on a drawing of a dictionary icon,
  `<GeometryDrawing Stroke="{ThemeResource AccentFillColorDefault}"/>`, did not compile: `ThemeResource.Apply` and
  `Remove` took only an element. They take any component.
