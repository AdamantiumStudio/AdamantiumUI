using System;

namespace Adamantium.UI.Core.Media.Animation;

/// <summary>
/// An animation of one property from a start value to an end value over <see cref="Duration"/>, shaped by
/// <see cref="Easing"/>; what the values are and how they are passed through is the derived type's. Start it with
/// <see cref="AnimatableUIComponent.BeginAnimation"/>.
/// </summary>
public abstract class PropertyAnimation
{
    public TimeSpan Duration { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Wait this long before the first iteration starts (the start value is held during the delay).</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    /// <summary>How many times to play; <see cref="double.PositiveInfinity"/> loops forever. Default 1.</summary>
    public double IterationCount { get; set; } = 1;

    /// <summary>When true, every other iteration plays backwards (end to start), i.e. ping-pong.</summary>
    public bool AutoReverse { get; set; }

    /// <summary>Easing curve; null means linear.</summary>
    public IEasingFunction Easing { get; set; }

    /// <summary>What the finished animation leaves behind: <see cref="FillBehavior.HoldEnd"/> (default) keeps the final
    /// value applied at Animation priority; <see cref="FillBehavior.Stop"/> clears it, releasing the property back to
    /// its underlying value and to direct sets (an ease-back tilt must not mask later hover writes).</summary>
    public FillBehavior FillBehavior { get; set; } = FillBehavior.HoldEnd;

    internal abstract Func<double, object> Interpolation();
}
