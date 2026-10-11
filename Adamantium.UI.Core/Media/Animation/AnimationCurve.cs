using System;

namespace Adamantium.UI.Core.Media.Animation;

/// <summary>A pure, immutable curve from elapsed seconds to one value per track, safe to evaluate on any thread; lets the
/// render thread animate while the loop thread is busy.</summary>
public sealed class AnimationCurve
{
    /// <summary>One animated property and its keyframe stops, sorted by cue. Cues are 0..1 within one iteration.</summary>
    public sealed class Track
    {
        public Track(AdamantiumProperty property, double[] cues, double[] values)
        {
            Property = property;
            Cues = cues;
            Values = values;
        }

        public AdamantiumProperty Property { get; }
        internal double[] Cues { get; }
        internal double[] Values { get; }
    }

    private readonly double _durationSeconds;
    private readonly double _delaySeconds;
    private readonly double _iterationCount;
    private readonly bool _autoReverse;
    private readonly IEasingFunction _easing;

    public AnimationCurve(Track[] tracks, double durationSeconds, double delaySeconds, double iterationCount,
        bool autoReverse, IEasingFunction easing)
    {
        Tracks = tracks;
        _durationSeconds = Math.Max(0.0001, durationSeconds);   // a zero duration would divide by zero on the first tick
        _delaySeconds = Math.Max(0.0, delaySeconds);
        _iterationCount = iterationCount;
        _autoReverse = autoReverse;
        _easing = easing;
    }

    public Track[] Tracks { get; }

    /// <summary>True when the curve loops forever and so never finishes on its own.</summary>
    public bool IsEndless => double.IsPositiveInfinity(_iterationCount);

    /// <summary>True once <paramref name="elapsedSeconds"/> is past the last iteration. An infinite animation never is.</summary>
    public bool IsFinished(double elapsedSeconds)
    {
        if (double.IsPositiveInfinity(_iterationCount)) return false;
        var active = elapsedSeconds - _delaySeconds;
        return active > 0.0 && active / _durationSeconds >= _iterationCount;
    }

    /// <summary>The value of <paramref name="track"/> at <paramref name="elapsedSeconds"/> since the animation began.
    /// Clamped at both ends: before the delay it holds the start value, past the end it holds the final one.</summary>
    public double Evaluate(Track track, double elapsedSeconds) =>
        Interpolate(track, CyclePosition(Progress(elapsedSeconds)));

    // Iterations completed so far (fractional), clamped to the last one. Still inside the delay -> 0, i.e. hold the start.
    private double Progress(double elapsedSeconds)
    {
        var active = elapsedSeconds - _delaySeconds;
        if (active <= 0.0) return 0.0;

        var cycles = active / _durationSeconds;
        return !double.IsPositiveInfinity(_iterationCount) && cycles >= _iterationCount ? _iterationCount : cycles;
    }

    /// <summary>The eased position within the current iteration, 0..1. Integer boundaries are the END of the previous
    /// iteration; with AutoReverse the odd iterations run backwards.</summary>
    private double CyclePosition(double progress)
    {
        var iteration = Math.Floor(progress);
        var localT = progress - iteration;
        if (localT == 0.0 && progress > 0.0)
        {
            iteration -= 1;
            localT = 1.0;
        }

        var reversed = _autoReverse && (long)iteration % 2 == 1;
        var pos = reversed ? 1.0 - localT : localT;
        return _easing?.Ease(pos) ?? pos;
    }

    private static double Interpolate(Track track, double position)
    {
        var cues = track.Cues;
        var values = track.Values;
        if (position <= cues[0])
        {
            return position < 0.0 && cues[0] == 0.0 && cues.Length > 1 ? Lerp(track, 0, position) : values[0];
        }

        if (position >= cues[^1])
        {
            return position > 1.0 && cues[^1] == 1.0 && cues.Length > 1 ? Lerp(track, cues.Length - 2, position) : values[^1];
        }

        for (var i = 0; i < cues.Length - 1; i++)
        {
            if (position <= cues[i + 1])
            {
                return Lerp(track, i, position);
            }
        }
        return values[^1];
    }

    private static double Lerp(Track track, int segment, double position)
    {
        var cues = track.Cues;
        var values = track.Values;
        var span = cues[segment + 1] - cues[segment];
        var t = span <= 0 ? 0.0 : (position - cues[segment]) / span;
        return values[segment] + (values[segment + 1] - values[segment]) * t;
    }
}
