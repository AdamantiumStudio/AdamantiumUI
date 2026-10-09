using System;

namespace Adamantium.UI.Core.Media.Animation;

/// <summary>
/// Animates the axis values of a variable font's text (<c>FontVariations</c>) from <see cref="From"/> to
/// <see cref="To"/>: each axis moves between its two values, and the text is drawn between the font's key instances on
/// the way, its own glyphs never filling the atlas (<see cref="FontVariationList.Between"/>).
/// </summary>
public sealed class FontVariationListAnimation : PropertyAnimation
{
    public FontVariationList From { get; set; }

    public FontVariationList To { get; set; }

    internal override Func<double, object> Interpolation()
    {
        var from = From;
        var to = To;
        return progress => FontVariationList.Between(from, to, progress);
    }
}
