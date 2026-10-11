using System;

namespace Adamantium.UI.Core.Media.Animation;

/// <summary>
/// An animation of one property from a start value to an end value over <see cref="Timeline.Duration"/>, shaped by
/// <see cref="Timeline.Easing"/>; what the values are and how they are passed through is the derived type's. Start it
/// from code with <see cref="AnimatableUIComponent.BeginAnimation"/>, or from markup in a trigger's
/// <c>RunAnimationAction</c>, naming the animated property in <see cref="Property"/>.
/// </summary>
public abstract class PropertyAnimation : Timeline
{
    public static readonly AdamantiumProperty PropertyProperty = AdamantiumProperty.Register(nameof(Property),
        typeof(string), typeof(PropertyAnimation), new PropertyMetadata(null, OnTimingChanged));

    public static readonly AdamantiumProperty FillBehaviorProperty = AdamantiumProperty.Register(nameof(FillBehavior),
        typeof(FillBehavior), typeof(PropertyAnimation), new PropertyMetadata(FillBehavior.HoldEnd, OnTimingChanged));

    private AdamantiumProperty _property;

    /// <summary>The NAME of the property this animation drives, resolved against the element it runs on (like
    /// <c>Setter.Property</c>) - what markup says. Code that starts it with <see cref="AnimatableUIComponent.BeginAnimation"/>
    /// names the property there instead.</summary>
    public string Property
    {
        get => GetValue(PropertyProperty) as string;
        set => SetValue(PropertyProperty, value);
    }

    /// <summary>What the finished animation leaves behind: <see cref="Media.Animation.FillBehavior.HoldEnd"/> (default) keeps
    /// the final value applied at Animation priority; <see cref="Media.Animation.FillBehavior.Stop"/> clears it, releasing the
    /// property back to its underlying value and to direct sets (an ease-back tilt must not mask later hover writes).</summary>
    public FillBehavior FillBehavior
    {
        get => GetValue<FillBehavior>(FillBehaviorProperty);
        set => SetValue(FillBehaviorProperty, value);
    }

    internal abstract Func<double, object> Interpolation();

    internal virtual AnimationCurve Curve(AdamantiumProperty property) => null;

    internal void Start(AdamantiumComponent target, AdamantiumProperty property, Action completed)
    {
        _property = property;
        Apply(target, completed);
    }

    /// <inheritdoc />
    protected override void Begin(AdamantiumComponent target, Action completed, double resumeElapsed)
    {
        var property = _property ?? target.GetProperty(Property);
        if (property == null)
        {
            Diagnostics.PropertyTrace.Log($"{target.GetType().Name} has no property {Property} for {GetType().Name}; nothing was animated.");
            return;
        }

        AnimationManager.Begin(target, property, this, completed, resumeElapsed);
    }
}
