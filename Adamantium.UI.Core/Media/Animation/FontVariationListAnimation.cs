using System;

namespace Adamantium.UI.Core.Media.Animation;

/// <summary>
/// Animates the axis values of a variable font's text (<c>FontVariations</c>) from <see cref="From"/> to
/// <see cref="To"/>: each axis moves between its two values, and the text is drawn between the font's key instances on
/// the way, its own glyphs never filling the atlas (<see cref="FontVariationList.Between"/>).
/// </summary>
public sealed class FontVariationListAnimation : PropertyAnimation
{
    public static readonly AdamantiumProperty FromProperty = AdamantiumProperty.Register(nameof(From),
        typeof(FontVariationList), typeof(FontVariationListAnimation), new PropertyMetadata(null, OnTimingChanged));

    public static readonly AdamantiumProperty ToProperty = AdamantiumProperty.Register(nameof(To),
        typeof(FontVariationList), typeof(FontVariationListAnimation), new PropertyMetadata(null, OnTimingChanged));

    /// <summary>The axis values the animation starts from.</summary>
    public FontVariationList From
    {
        get => GetValue(FromProperty) as FontVariationList;
        set => SetValue(FromProperty, value);
    }

    /// <summary>The axis values the animation ends at.</summary>
    public FontVariationList To
    {
        get => GetValue(ToProperty) as FontVariationList;
        set => SetValue(ToProperty, value);
    }

    internal override Func<double, object> Interpolation()
    {
        var from = From;
        var to = To;
        return progress => FontVariationList.Between(from, to, progress);
    }
}
