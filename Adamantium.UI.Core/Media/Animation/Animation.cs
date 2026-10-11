using System;
using System.Collections.Generic;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Core.Media.Animation;

/// <summary>
/// A keyframe animation (CSS <c>@keyframes</c> analog, own implementation): interpolates the double properties named in
/// its <see cref="KeyFrames"/>' setters through those keyframes over <see cref="Duration"/>, honoring
/// <see cref="Delay"/>, <see cref="IterationCount"/> (<see cref="double.PositiveInfinity"/> = loop) and
/// <see cref="AutoReverse"/> (ping-pong). Start it with <see cref="Apply"/>. Its timing is bindable: it binds against the
/// element it runs on, and a change while it runs restarts it where it was.
/// </summary>
public class Animation : AdamantiumComponent
{
    public static readonly AdamantiumProperty DurationProperty = AdamantiumProperty.Register(nameof(Duration),
        typeof(TimeSpan), typeof(Animation), new PropertyMetadata(TimeSpan.FromMilliseconds(250), OnTimingChanged));

    public static readonly AdamantiumProperty DelayProperty = AdamantiumProperty.Register(nameof(Delay),
        typeof(TimeSpan), typeof(Animation), new PropertyMetadata(TimeSpan.Zero, OnTimingChanged));

    public static readonly AdamantiumProperty IterationCountProperty = AdamantiumProperty.Register(nameof(IterationCount),
        typeof(double), typeof(Animation), new PropertyMetadata(1.0, OnTimingChanged));

    public static readonly AdamantiumProperty AutoReverseProperty = AdamantiumProperty.Register(nameof(AutoReverse),
        typeof(bool), typeof(Animation), new PropertyMetadata(false, OnTimingChanged));

    public static readonly AdamantiumProperty EasingProperty = AdamantiumProperty.Register(nameof(Easing),
        typeof(IEasingFunction), typeof(Animation), new PropertyMetadata(null, OnTimingChanged));

    private WeakReference<AdamantiumComponent> _target;
    private Action _completed;

    public TimeSpan Duration
    {
        get => GetValue<TimeSpan>(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    /// <summary>Wait this long before the first iteration (start values held during the delay).</summary>
    public TimeSpan Delay
    {
        get => GetValue<TimeSpan>(DelayProperty);
        set => SetValue(DelayProperty, value);
    }

    /// <summary>How many iterations to play; <see cref="double.PositiveInfinity"/> loops forever. Default 1.</summary>
    public double IterationCount
    {
        get => GetValue<double>(IterationCountProperty);
        set => SetValue(IterationCountProperty, value);
    }

    /// <summary>When true, every other iteration plays backwards (ping-pong).</summary>
    public bool AutoReverse
    {
        get => GetValue<bool>(AutoReverseProperty);
        set => SetValue(AutoReverseProperty, value);
    }

    /// <summary>Easing applied across the iteration's timeline; null means linear.</summary>
    public IEasingFunction Easing
    {
        get => GetValue(EasingProperty) as IEasingFunction;
        set => SetValue(EasingProperty, value);
    }

    /// <summary>The keyframes, by <see cref="KeyFrame.Cue"/> (order is normalized at run time). Markup content, so
    /// they are written directly inside &lt;Animation&gt;.</summary>
    [Content]
    public List<KeyFrame> KeyFrames { get; } = new();

    /// <summary>Starts this animation on <paramref name="target"/>; <paramref name="completed"/> fires when it ends
    /// (never, for an infinite animation). The target is any <see cref="AdamantiumComponent"/> - a UI element OR a
    /// non-visual component such as a <c>GradientStop</c> (whose owning element repaints via its brush subscription).
    /// The target is also what the animation's own bindings resolve against; an animation without bindings never takes
    /// it as a parent, so one shared by a style's elements holds none of them.</summary>
    public void Apply(AdamantiumComponent target, Action completed = null, double resumeElapsed = 0)
    {
        if (!ReferenceEquals(InheritanceParent, target) && Data.BindingEngine.HasBindings(this))
        {
            InheritanceParent = target;
            Data.BindingEngine.RefreshBindings(this);
        }

        _target = new WeakReference<AdamantiumComponent>(target);
        _completed = completed;
        Prepare();
        AnimationManager.BeginKeyFrame(target, this, completed, resumeElapsed);
    }

    /// <summary>Hook for a prebuilt animation TYPE (e.g. <c>PulseAnimation</c>) to synthesize its <see cref="KeyFrames"/>
    /// from friendly properties (Min/Max/...) just before it runs. The base keyframe animation authors its frames in
    /// markup, so this is a no-op for it.</summary>
    protected virtual void Prepare() { }

    /// <summary>A derived type calls this from the change of a property its <see cref="Prepare"/> reads, so a running
    /// animation picks the new value up without losing its place.</summary>
    protected static void OnTimingChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        var animation = (Animation)component;
        if (animation._target != null && animation._target.TryGetTarget(out var target)
            && AnimationManager.ElapsedOf(animation, target) is { } elapsed)
        {
            animation.Apply(target, animation._completed, elapsed);
        }
    }
}
