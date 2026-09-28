using Adamantium.UI.Core.Resources;

namespace Adamantium.UI.Core.Media.Animation;

/// <summary>A looping "breathe" animation easing a double <see cref="Property"/> between <see cref="Min"/> and
/// <see cref="Max"/> and back, e.g. <c>&lt;PulseAnimation Property="Opacity" Min="0.05" Max="0.16"/&gt;</c>.</summary>
public class PulseAnimation : Animation
{
    /// <summary>Name of the double property to pulse on the animation's target (e.g. <c>Opacity</c>).</summary>
    public string Property { get; set; }

    /// <summary>The low end of the pulse.</summary>
    public double Min { get; set; }

    /// <summary>The high end of the pulse.</summary>
    public double Max { get; set; }

    protected override void Prepare()
    {
        // Bake the pulse: Min -> Max over the duration (eased), AutoReverse brings it back, loop forever.
        AutoReverse = true;
        IterationCount = double.PositiveInfinity;
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
