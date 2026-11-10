using Adamantium.Mathematics;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Pdf.Tests;

/// <summary>A run of glyphs drawn, inside <see cref="Depth"/> groups.</summary>
internal sealed record DrawnGlyphs(GlyphRun Run, Brush Fill, Pen Stroke, Matrix4x4<float> Transform, int Depth);
