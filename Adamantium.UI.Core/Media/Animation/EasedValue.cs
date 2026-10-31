using System;

namespace Adamantium.UI.Core.Media.Animation;

/// <summary>
/// Eases one number of a camera toward a target on the animation heartbeat, each step a share of the way that depends on
/// the frame's time, so quick wheel notches or drags add up into one glide and nothing jumps. A zoom is eased in
/// logarithmic units of the owner's choosing (log2 of a map's scale, log10 of a fractal's, ln of a canvas's), a turn in
/// degrees. The owner reads and writes the number and re-anchors its own camera on every step, which keeps the point
/// under the cursor still however the camera is stored.
/// </summary>
public sealed class EasedValue
{
    private readonly Func<double> _read;
    private readonly Action<double> _apply;
    private readonly Action _settled;
    private double _target;
    private bool _active;
    private bool _registered;
    private long _registeredAt;

    /// <summary>Makes an ease that reads the live value with <paramref name="read"/>, writes each step with
    /// <paramref name="apply"/> and calls <paramref name="settled"/> once the value comes to rest.</summary>
    public EasedValue(Func<double> read, Action<double> apply, Action settled = null)
    {
        _read = read;
        _apply = apply;
        _settled = settled;
    }

    /// <summary>How fast the value closes on the target, per second: higher snaps sooner, lower glides longer.</summary>
    public double Rate { get; set; } = 12.0;

    /// <summary>How close to the target, in the value's own units, counts as there.</summary>
    public double Tolerance { get; set; } = 1e-3;

    /// <summary>Whether the value is still on its way to the target.</summary>
    public bool IsActive => _active;

    /// <summary>Where the value is going - the live value when nothing is easing. What the next notch adds to.</summary>
    public double Target => _active ? _target : _read();

    /// <summary>Sends the value toward <paramref name="target"/>, kept between <paramref name="minimum"/> and
    /// <paramref name="maximum"/> - at the minimum when the maximum is below it. A target that is not a number is
    /// ignored.</summary>
    public void EaseTo(double target, double minimum = double.NegativeInfinity, double maximum = double.PositiveInfinity)
    {
        var clamped = Math.Clamp(target, minimum, Math.Max(minimum, maximum));
        if (!double.IsFinite(clamped))
        {
            return;
        }

        _target = clamped;
        _active = true;
        if (_registered && _registeredAt == AnimationManager.Resets)
        {
            return;
        }

        _registered = true;
        _registeredAt = AnimationManager.Resets;
        AnimationManager.AddTicker(Advance);
    }

    /// <summary>Adds <paramref name="delta"/> to the target - one wheel notch, one press of a plus button.</summary>
    public void EaseBy(double delta, double minimum = double.NegativeInfinity, double maximum = double.PositiveInfinity) =>
        EaseTo(Target + delta, minimum, maximum);

    /// <summary>Stops where the value is now; a value set straight, or a camera placed by code, ends the glide.</summary>
    public void Stop()
    {
        _active = false;
    }

    private bool Advance(double seconds)
    {
        if (!_active)
        {
            _registered = false;
            return true;
        }

        var current = _read();
        var next = current + (_target - current) * (1.0 - Math.Exp(-Rate * seconds));
        if (!double.IsFinite(next) || Math.Abs(_target - next) < Tolerance)
        {
            next = _target;
        }

        _apply(next);
        if (next != _target && (next == current || _read() != current))
        {
            return false;
        }

        _active = false;
        _registered = false;
        _settled?.Invoke();
        return true;
    }
}
