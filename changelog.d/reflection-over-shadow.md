### Fixed

- An element's reflection lay under its shadow, darkened by it where the shadow fell across the floor. A reflected
  element's shadow is now drawn apart from it and lies on the floor as a glossy floor shows it: the floor's own color,
  darkened by the shadow, plus the light it mirrors. The reflection does not mirror the shadow - it is on the floor -
  and adds its light over it; the shadow fades by how much of a mirror the floor is (the reflection's opacity), alike
  under the reflection and beside it, and is gone on a perfect mirror. The glow stays with the element and is mirrored
  with it. A soft-edged reflected element keeps all its bands apart, as before, and lays its reflection over them.
  The shadows of elements inside a reflected one are still drawn with them, in front of the reflection.
