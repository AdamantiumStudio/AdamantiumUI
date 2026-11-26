using Adamantium.Mathematics;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Core.Graphics.Vector;

/// <summary>A stretch of text drawn by its glyphs' outlines: one path in one paint and one thickening.</summary>
/// <param name="Outline">The outlines, each where its glyph is drawn, filled by the nonzero rule: their curves alone
/// (<see cref="CurveGeometry"/>), bounds and all.</param>
/// <param name="Color">The stretch's own color; null for the text's brush.</param>
/// <param name="Thickening">How far a synthesized bold thickens each side; 0 for none.</param>
internal readonly record struct TextOutline(Geometry Outline, Color? Color, double Thickening)
{
    /// <summary>What thickens a synthesized bold - a line as wide as both sides, in the fill, with round corners, as an
    /// outline grown evenly; null for none.</summary>
    public Pen Thickener(Brush fill) =>
        Thickening > 0 ? new Pen(fill, 2 * Thickening, penLineJoin: PenLineJoin.Round) : null;
}
