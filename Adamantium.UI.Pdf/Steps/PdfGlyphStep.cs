using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Pdf.Steps;

/// <summary>A run of glyphs filled, stroked or both.</summary>
internal sealed class PdfGlyphStep(GlyphRun run, Brush fill, Pen stroke, Matrix4x4F matrix) : PdfDrawStep
{
    public GlyphRun Run { get; } = run;

    public Brush Fill { get; } = fill;

    public Pen Stroke { get; } = stroke;

    public Matrix4x4F Matrix { get; } = matrix;

    public override void Draw(IDrawingSession session, Matrix4x4F transform) =>
        session.DrawGlyphs(Run, Fill, Stroke, Matrix * transform);
}
