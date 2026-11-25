### Fixed

- An element's reflection lay under its shadow, darkened by it where the shadow fell across the floor. A reflected
  element's shadow is now drawn apart from it and lies on the floor under the reflection: the reflection does not
  mirror it - it is on the floor - and does not let it show through, whatever its opacity, which fades only the
  reflection; past the reflection, the shadow is as before. The glow stays with the element and is mirrored with it.
  A soft-edged reflected element keeps all its bands apart, as before, and lays its reflection over them. The shadows
  of elements inside a reflected one are still drawn with them, in front of the reflection.
