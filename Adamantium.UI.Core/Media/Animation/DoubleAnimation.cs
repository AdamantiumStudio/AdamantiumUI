using System;

namespace Adamantium.UI.Core.Media.Animation;

/// <summary>
/// Describes an animation of a <see cref="double"/> AdamantiumProperty from <see cref="From"/> to <see cref="To"/> over
/// <see cref="PropertyAnimation.Duration"/>, shaped by <see cref="PropertyAnimation.Easing"/>. Start it with
/// <see cref="AnimatableUIComponent.BeginAnimation"/>.
/// </summary>
public sealed class DoubleAnimation : PropertyAnimation
{
    public double From { get; set; }

    public double To { get; set; }

    internal override Func<double, object> Interpolation()
    {
        var from = From;
        var to = To;
        return progress => from + (to - from) * progress;
    }

    internal override AnimationCurve Curve(AdamantiumProperty property) =>
        new([new AnimationCurve.Track(property, [0.0, 1.0], [From, To])], Duration.TotalSeconds, Delay.TotalSeconds,
            IterationCount, AutoReverse, Easing);
}
