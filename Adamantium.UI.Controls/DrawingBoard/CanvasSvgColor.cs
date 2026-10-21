using System;
using Adamantium.Fonts;
using Adamantium.Mathematics;

namespace Adamantium.UI.Controls.DrawingBoard;

/// <summary>Reads the colors a drawing file states - the several spellings CSS allows, which is what an SVG carries.
/// <para>Null for one it cannot read, and that is deliberate: a color it does not understand is not black, and
/// guessing would repaint somebody's drawing while claiming to have opened it.</para></summary>
public static class CanvasSvgColor
{
    public static Color? Of(string text)
    {
        if (String.IsNullOrWhiteSpace(text)) return null;

        var value = text.Trim();

        if (value.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("transparent", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Css.TryParseColor(value, out var color) ? color : null;
    }
}
