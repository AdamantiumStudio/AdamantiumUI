using Adamantium.Mathematics;
using Adamantium.UI.Controls.Canvases;
using Adamantium.UI.Core.Graphics;

namespace Adamantium.UITests;

/// <summary>Draws one item the way a layer of the canvas does - with placements of its own, held here - into a session a
/// test can read. What it records is in the item's LOCAL units; the placement carries it to the screen.</summary>
internal static class CanvasItemDrawing
{
    public static CanvasDrawing Drawing(this ICanvasItem item, IDrawingSession session, InfiniteCanvas canvas) =>
        new CanvasPlacements().For(session, canvas, item);

    public static void Render(this ICanvasItem item, IDrawingSession session, InfiniteCanvas canvas) =>
        item.Render(item.Drawing(session, canvas));

    public static void Render(this CurveItem item, IDrawingSession session, InfiniteCanvas canvas,
        Vector2<double> extra) =>
        item.Render(item.Drawing(session, canvas), extra);

    /// <summary>Where a point the item drew - in its local units - lands on screen through its placement.</summary>
    public static Vector2<double> OnScreen(this Placement placement, Vector2<double> local)
    {
        var m = placement.Matrix;
        return new Vector2<double>(local.X * m.M11 + local.Y * m.M21 + m.M41, local.X * m.M12 + local.Y * m.M22 + m.M42);
    }

    /// <summary>The same, for a point handed over in single precision.</summary>
    public static Vector2<double> OnScreen(this Placement placement, Vector2F local) =>
        placement.OnScreen(new Vector2<double>(local.X, local.Y));
}
