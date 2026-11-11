using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Media;

/// <summary>A corner of a <see cref="ColoredMesh"/> triangle: where it stands and its color, the color running smoothly
/// across the triangle between its corners.</summary>
public readonly record struct ColoredVertex(Vector2<double> Position, Color Color);
