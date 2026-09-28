using System;
using Adamantium.Mathematics;

namespace Adamantium.UI.Controls.DrawingBoard;

/// <summary>Where the three points of an arrow head fall, shared by every arrow. Measured in line thicknesses, so the head
/// scales with its line.</summary>
public static class ArrowHead
{
    /// <summary>The tip, and the two corners of the head's base, for a head pointing from <paramref name="from"/>
    /// toward <paramref name="tip"/>. False when the two points are on top of each other and there is no direction to
    /// point in.</summary>
    public static bool Points(Vector2 from, Vector2 tip, double thickness, double lengthRatio, double widthRatio,
        out Vector2 left, out Vector2 right)
    {
        left = right = tip;

        var dx = tip.X - from.X;
        var dy = tip.Y - from.Y;
        var span = Math.Sqrt(dx * dx + dy * dy);
        if (span <= 1e-9) return false;

        dx /= span;
        dy /= span;

        // Never longer than the line it sits on: on a very short arrow a head of the stated length would start behind
        // the tail and point the wrong way.
        var length = Math.Min(Math.Max(thickness, 1e-6) * lengthRatio, span);
        var half = Math.Max(thickness, 1e-6) * widthRatio / 2;

        var baseX = tip.X - dx * length;
        var baseY = tip.Y - dy * length;

        left = new Vector2(baseX - dy * half, baseY + dx * half);
        right = new Vector2(baseX + dy * half, baseY - dx * half);

        return true;
    }
}
