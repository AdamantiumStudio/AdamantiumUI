using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Controls.Text;

/// <summary>A dotted or dashed line of laid-out text - a decoration or a paragraph's border - as text controls draw
/// it.</summary>
public static class DashedLine
{
    /// <summary>Draws the line filling <paramref name="band"/> across, its dashes and gaps in turn along it; down runs it
    /// down the band, as along vertical text or a border at a paragraph's side.</summary>
    public static void Draw(IDrawingSession session, Rect band, Brush brush, IReadOnlyList<double> dashes, bool down)
    {
        var thickness = down ? band.Width : band.Height;
        var pen = new Pen(brush, thickness, dashStrokeArray: dashes, dashStartCap: PenLineCap.Flat,
            dashEndCap: PenLineCap.Flat);
        var middle = (down ? band.X : band.Y) + thickness / 2;
        session.DrawLine(down ? new Vector2<double>(middle, band.Y) : new Vector2<double>(band.X, middle),
            down ? new Vector2<double>(middle, band.Y + band.Height) : new Vector2<double>(band.X + band.Width, middle),
            pen);
    }

    /// <summary>Whether a paragraph's border line in <paramref name="band"/> runs down: one at a side, taller than it is
    /// wide.</summary>
    public static bool RunsDown(RectangleF band) => band.Height > band.Width;
}
