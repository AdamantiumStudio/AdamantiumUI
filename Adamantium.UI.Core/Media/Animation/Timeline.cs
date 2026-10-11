using System;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Core.Media.Animation;

/// <summary>
/// What every animation has: how long one iteration takes, how long it waits, how often it repeats, whether every other
/// iteration plays backwards, and its easing. The timing is bindable: an animation binds against the element it runs on,
/// and a change while it runs restarts it at the same point of its cycle. Start one with <see cref="Apply"/>.
/// </summary>
public abstract class Timeline : AdamantiumComponent
{
    public static readonly AdamantiumProperty DurationProperty = AdamantiumProperty.Register(nameof(Duration),
        typeof(TimeSpan), typeof(Timeline), new PropertyMetadata(TimeSpan.FromMilliseconds(250), OnTimingChanged));

    public static readonly AdamantiumProperty DelayProperty = AdamantiumProperty.Register(nameof(Delay),
        typeof(TimeSpan), typeof(Timeline), new PropertyMetadata(TimeSpan.Zero, OnTimingChanged));

    public static readonly AdamantiumProperty IterationCountProperty = AdamantiumProperty.Register(nameof(IterationCount),
        typeof(double), typeof(Timeline), new PropertyMetadata(1.0, OnTimingChanged));

    public static readonly AdamantiumProperty AutoReverseProperty = AdamantiumProperty.Register(nameof(AutoReverse),
        typeof(bool), typeof(Timeline), new PropertyMetadata(false, OnTimingChanged));

    public static readonly AdamantiumProperty EasingProperty = AdamantiumProperty.Register(nameof(Easing),
        typeof(IEasingFunction), typeof(Timeline), new PropertyMetadata(null, OnTimingChanged));

    private WeakReference<AdamantiumComponent> _target;
    private Action _completed;

    /// <summary>How long one iteration takes.</summary>
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

    /// <summary>Starts this animation on <paramref name="target"/>; <paramref name="completed"/> fires when it ends
    /// (never, for an infinite animation). The target is any <see cref="AdamantiumComponent"/> - a UI element OR a
    /// non-visual component such as a <c>GradientStop</c> (whose owning element repaints via its brush subscription).
    /// The target is also what the animation's own bindings resolve against; an animation without bindings never takes
    /// it as a parent.</summary>
    public void Apply(AdamantiumComponent target, Action completed = null, double resumeElapsed = 0)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!ReferenceEquals(InheritanceParent, target) && Data.BindingEngine.HasBindings(this))
        {
            if (InheritanceParent != null)
            {
                InheritanceParent.PropertyChanged -= OnTargetPropertyChanged;
            }

            InheritanceParent = target;
            target.PropertyChanged += OnTargetPropertyChanged;
            Data.BindingEngine.RefreshBindings(this);
        }

        _target = new WeakReference<AdamantiumComponent>(target);
        _completed = completed;
        Begin(target, completed, resumeElapsed);
    }

    /// <summary>A copy of this animation: the same values, the same bindings and the same content, running on its own.
    /// What a style's trigger gives each element it applies to, so the element's bindings are its own.</summary>
    public Timeline Clone()
    {
        var copy = CreateInstance();
        foreach (var property in AdamantiumPropertyMap.GetRegistered(this))
        {
            if (Data.BindingEngine.GetBindingExpression(this, property) is { BindingBase: { } binding })
            {
                copy.SetBinding(property, binding);
                continue;
            }

            var source = GetValueSource(property);
            if (source is ValuePriority.Local or ValuePriority.Trigger or ValuePriority.Template or ValuePriority.Style
                or ValuePriority.TypeDefault)
            {
                copy.SetValue(property, GetValue(property), source);
            }
        }

        CopyContentTo(copy);
        return copy;
    }

    /// <summary>Plays the animation on <paramref name="target"/>, <paramref name="resumeElapsed"/> seconds in.</summary>
    protected abstract void Begin(AdamantiumComponent target, Action completed, double resumeElapsed);

    /// <summary>A new, empty instance for <see cref="Clone"/>; a type without a parameterless constructor overrides it.</summary>
    protected virtual Timeline CreateInstance() => (Timeline)Activator.CreateInstance(GetType());

    /// <summary>Copies what is not a registered property (keyframes) into <paramref name="copy"/>.</summary>
    protected virtual void CopyContentTo(Timeline copy) { }

    private void OnTargetPropertyChanged(object sender, AdamantiumPropertyChangedEventArgs e)
    {
        if (e.Property == FundamentalUIComponent.DataContextProperty)
        {
            Data.BindingEngine.RefreshBindings(this);
        }
    }

    /// <summary>The change callback of every timing property: a running animation picks the new value up without losing
    /// its place. A derived type registers its own properties with it.</summary>
    protected static void OnTimingChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        var timeline = (Timeline)component;
        if (timeline._target != null && timeline._target.TryGetTarget(out var target)
            && AnimationManager.ElapsedOf(timeline, target) is { } elapsed && !timeline.HasEndedAt(elapsed))
        {
            timeline.Begin(target, timeline._completed, elapsed);
        }
    }

    private bool HasEndedAt(double elapsed) =>
        !double.IsPositiveInfinity(IterationCount)
        && elapsed >= Delay.TotalSeconds + Duration.TotalSeconds * IterationCount;
}
