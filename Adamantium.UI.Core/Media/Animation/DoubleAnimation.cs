using System;

namespace Adamantium.UI.Core.Media.Animation;

/// <summary>
/// Describes an animation of a <see cref="double"/> AdamantiumProperty from <see cref="From"/> to <see cref="To"/> over
/// <see cref="Timeline.Duration"/>, shaped by <see cref="Timeline.Easing"/>. Start it with
/// <see cref="AnimatableUIComponent.BeginAnimation"/>, or from markup:
/// <c>&lt;DoubleAnimation Property="Opacity" From="0" To="1"/&gt;</c>.
/// </summary>
public sealed class DoubleAnimation : PropertyAnimation
{
    public static readonly AdamantiumProperty FromProperty = AdamantiumProperty.Register(nameof(From),
        typeof(double), typeof(DoubleAnimation), new PropertyMetadata(0.0, OnTimingChanged));

    public static readonly AdamantiumProperty ToProperty = AdamantiumProperty.Register(nameof(To),
        typeof(double), typeof(DoubleAnimation), new PropertyMetadata(0.0, OnTimingChanged));

    /// <summary>The value the animation starts from.</summary>
    public double From
    {
        get => GetValue<double>(FromProperty);
        set => SetValue(FromProperty, value);
    }

    /// <summary>The value the animation ends at.</summary>
    public double To
    {
        get => GetValue<double>(ToProperty);
        set => SetValue(ToProperty, value);
    }

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
