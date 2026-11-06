using Adamantium.UI.Controls.Canvases;
using Adamantium.UI.Core.Graphics;

namespace Adamantium.XamlTests;

/// <summary>Draws one item the way a layer of the canvas does - with placements of its own, held here - into a session a
/// test can read. What it records is in the item's LOCAL units; the placement carries it to the screen.</summary>
internal static class CanvasItemDrawing
{
    public static void Render(this ICanvasItem item, IDrawingSession session, InfiniteCanvas canvas) =>
        item.Render(new CanvasPlacements().For(session, canvas, item));
}
