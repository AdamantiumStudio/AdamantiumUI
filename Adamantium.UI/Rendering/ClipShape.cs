using Adamantium.Mathematics;

namespace Adamantium.UI.Rendering;

/// <summary>A clip as a clip slot holds it, in device pixels.</summary>
/// <param name="Box">Its rectangle: origin in xy, size in zw; a size of zero is no clip.</param>
/// <param name="Radii">Its corner radii: top left, top right, bottom right, bottom left.</param>
internal readonly record struct ClipShape(Vector4F Box, Vector4F Radii);
