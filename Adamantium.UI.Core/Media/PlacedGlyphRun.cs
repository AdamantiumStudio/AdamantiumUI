using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Media;

/// <summary>A run of placed glyphs and where it goes in the drawing's units: what a group is cut to by its text.</summary>
public sealed record PlacedGlyphRun(GlyphRun Run, Matrix4x4F Transform);
