using System.Collections.Generic;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Drawings;
using Adamantium.UI.Pdf.Steps;

namespace Adamantium.UI.Pdf;

/// <summary>Draws read out of a PDF content stream, in the page's units: a soft mask's picture, a tiling pattern's cell.
/// Replays them as they were read; it is never changed after.</summary>
internal sealed class PdfContentDrawing(IReadOnlyList<PdfDrawStep> steps, Rect bounds) : Drawing
{
    public IReadOnlyList<PdfDrawStep> Steps { get; } = steps;

    public override Rect Bounds { get; } = bounds;

    public override void Render(IDrawingSession session, Matrix4x4F transform, Brush currentColor)
    {
        foreach (var step in Steps)
        {
            step.Draw(session, transform);
        }
    }
}
