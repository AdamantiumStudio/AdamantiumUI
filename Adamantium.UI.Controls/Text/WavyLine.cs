using System;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Controls.Text;

/// <summary>The wavy line under a misspelled word, as text boxes and editors draw it.</summary>
public static class WavyLine
{
    /// <summary>Draws the line along <paramref name="band"/>, the band a text layout gives a squiggle; down runs it
    /// along vertical text.</summary>
    public static void Draw(IDrawingSession session, RectangleF band, Brush brush, bool down)
    {
        var depth = down ? band.Width : band.Height;
        var thickness = depth / 3;
        var pen = new Pen(brush, thickness);
        var step = depth;
        var top = (down ? band.X : band.Y) + thickness / 2;
        var bottom = (down ? band.X : band.Y) + depth - thickness / 2;
        var x = (double)(down ? band.Y : band.X);
        var end = down ? band.Bottom : band.Right;
        var up = false;
        while (x < end)
        {
            var next = Math.Min(x + step, end);
            var fraction = (next - x) / step;
            var from = up ? bottom : top;
            var to = from + (up ? top - bottom : bottom - top) * fraction;
            session.DrawLine(down ? new Vector2(from, x) : new Vector2(x, from),
                down ? new Vector2(to, next) : new Vector2(next, to), pen);
            x = next;
            up = !up;
        }
    }
}
