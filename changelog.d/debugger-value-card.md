### Fixed

- `{ThemeResource}` on a drawing of a dictionary icon,
  `<GeometryDrawing Stroke="{ThemeResource AccentFillColorDefault}"/>`, did not compile: `ThemeResource.Apply` and
  `Remove` took only an element. They take any component; one outside the tree takes the application's theme current
  when it connects and follows that theme's changes, the accent among them - with `x:Shared="False"`, each use of the
  icon is built for the theme then in force.
