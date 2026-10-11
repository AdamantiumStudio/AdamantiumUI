using Adamantium.UI.Core.Resources;

namespace Adamantium.UI.Core.Media.Animation;

/// <summary>A looping "breathe" animation easing a double <see cref="Property"/> between <see cref="Min"/> and
/// <see cref="Max"/> and back, e.g. <c>&lt;PulseAnimation Property="Opacity" Min="0.05" Max="0.16"/&gt;</c>.</summary>
public class PulseAnimation : Animation
{
    public static readonly AdamantiumProperty PropertyProperty = AdamantiumProperty.Register(nameof(Property),
        typeof(string), typeof(PulseAnimation), new PropertyMetadata(null, OnTimingChanged));

    public static readonly AdamantiumProperty MinProperty = AdamantiumProperty.Register(nameof(Min),
        typeof(double), typeof(PulseAnimation), new PropertyMetadata(0.0, OnTimingChanged));

    public static readonly AdamantiumProperty MaxProperty = AdamantiumProperty.Register(nameof(Max),
        typeof(double), typeof(PulseAnimation), new PropertyMetadata(0.0, OnTimingChanged));

    static PulseAnimation()
    {
        AutoReverseProperty.OverrideMetadata(typeof(PulseAnimation), new PropertyMetadata(true, OnTimingChanged));
        IterationCountProperty.OverrideMetadata(typeof(PulseAnimation),
            new PropertyMetadata(double.PositiveInfinity, OnTimingChanged));
    }

    /// <summary>Name of the double property to pulse on the animation's target (e.g. <c>Opacity</c>).</summary>
    public string Property
    {
        get => GetValue(PropertyProperty) as string;
        set => SetValue(PropertyProperty, value);
    }

    /// <summary>The low end of the pulse.</summary>
    public double Min
    {
        get => GetValue<double>(MinProperty);
        set => SetValue(MinProperty, value);
    }

    /// <summary>The high end of the pulse.</summary>
    public double Max
    {
        get => GetValue<double>(MaxProperty);
        set => SetValue(MaxProperty, value);
    }

    protected override void Prepare()
    {
        // Min -> Max over the duration (eased); the type's AutoReverse and endless count defaults bring it back forever.
        KeyFrames.Clear();
        KeyFrames.Add(Frame(0, Min));
        KeyFrames.Add(Frame(1, Max));
    }

    private KeyFrame Frame(double cue, double value)
    {
        var frame = new KeyFrame { Cue = cue };
        frame.Setters.Add(new Setter(Property, value));
        return frame;
    }
}
