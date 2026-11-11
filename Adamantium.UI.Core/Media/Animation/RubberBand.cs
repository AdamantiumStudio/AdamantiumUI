using System;

namespace Adamantium.UI.Core.Media.Animation;

/// <summary>How a view pulled past its edge by a hand gives way: the further past, the less a move of the hand moves
/// it, never farther than <c>span</c> however far it is pulled.</summary>
public static class RubberBand
{
    /// <summary>How hard the band pulls back: the share of the hand's move it shows right at the edge.</summary>
    public const double Stiffness = 0.55;

    /// <summary>How far past the edge the view is shown with the hand <paramref name="past"/> past it, along an axis
    /// <paramref name="span"/> long.</summary>
    public static double Stretch(double past, double span) =>
        past == 0 || span <= 0 ? 0 : Math.Sign(past) * (1 - 1 / (Math.Abs(past) * Stiffness / span + 1)) * span;

    /// <summary>How far past the edge the hand is, the view shown <paramref name="shown"/> past it: the inverse of
    /// <see cref="Stretch"/>, so a view caught on its way back is taken where it is.</summary>
    public static double Unstretch(double shown, double span)
    {
        if (shown == 0 || span <= 0) return 0;

        var left = 1 - Math.Min(Math.Abs(shown) / span, 0.999);
        return Math.Sign(shown) * (1 / left - 1) * span / Stiffness;
    }

    /// <summary>How much of the hand's move the band shows with the hand <paramref name="past"/> past the edge; all of
    /// it inside.</summary>
    public static double Slope(double past, double span)
    {
        if (past == 0 || span <= 0) return 1;

        var stretch = Math.Abs(past) * Stiffness / span + 1;
        return Stiffness / (stretch * stretch);
    }
}
