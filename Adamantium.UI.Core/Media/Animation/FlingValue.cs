using System;

namespace Adamantium.UI.Core.Media.Animation;

/// <summary>
/// One number thrown by the hand: tracked while a gesture moves it, it coasts on with friction after the hand lifts
/// if it was still moving fast - the scalar counterpart of <see cref="PanInertia"/>'s flick, for what is not a position,
/// a pinched zoom among them. Read and written through the owner, which also clamps it; a step that does not move ends
/// the coast.
/// </summary>
public sealed class FlingValue
{
    private const double StuckChange = 1e-6;

    private readonly Func<double> _read;
    private readonly Action<double> _apply;
    private readonly Action _settled;
    private double _velocity;
    private double _trackedVelocity;
    private double _lastValue;
    private uint _lastTimestamp;
    private bool _hasLastMove;
    private bool _active;
    private bool _registered;
    private long _registeredAt;

    /// <summary>Makes a fling that reads the value with <paramref name="read"/> and moves it with
    /// <paramref name="apply"/>; <paramref name="settled"/>, when given, is told when a coast comes to rest by itself -
    /// not when it is stopped.</summary>
    public FlingValue(Func<double> read, Action<double> apply, Action settled = null)
    {
        _read = read;
        _apply = apply;
        _settled = settled;
    }

    /// <summary>How fast a coast slows, per second: higher stops it sooner, lower coasts further.</summary>
    public double Friction { get; set; } = 6.0;

    /// <summary>Speed, in units per second, below which a coast counts as stopped.</summary>
    public double MinSpeed { get; set; } = 0.05;

    /// <summary>Release speed, in units per second, a coast needs to start.</summary>
    public double StartSpeed { get; set; } = 0.5;

    /// <summary>Whether it is still coasting.</summary>
    public bool IsActive => _active;

    /// <summary>Forgets the moves seen so far - a new gesture starts a new throw.</summary>
    public void ResetTracking()
    {
        _hasLastMove = false;
        _trackedVelocity = 0;
    }

    /// <summary>Notes where the gesture has moved the value, at the time the hand made the move, in milliseconds.
    /// Timed by the moves rather than by frames, so a frame's worth of moves handled at once does not read as a throw.
    /// </summary>
    public void Track(uint timestamp)
    {
        var current = _read();
        if (_hasLastMove && timestamp != _lastTimestamp)
        {
            var seconds = unchecked(timestamp - _lastTimestamp) / 1000.0;
            _trackedVelocity = _trackedVelocity * Flick.KeptOfLast +
                               (current - _lastValue) / seconds * (1 - Flick.KeptOfLast);
        }

        _hasLastMove = true;
        _lastTimestamp = timestamp;
        _lastValue = current;
    }

    /// <summary>The hand lifted at <paramref name="timestamp"/>: coasts on if the value was still moving fast; one that
    /// had already stopped does not. True when a coast started.</summary>
    public bool Release(uint timestamp)
    {
        if (!_hasLastMove || Math.Abs(_trackedVelocity) <= StartSpeed ||
            unchecked(timestamp - _lastTimestamp) > Flick.RestMilliseconds)
        {
            return false;
        }

        _velocity = _trackedVelocity;
        _active = true;

        if (!_registered || _registeredAt != AnimationManager.Resets)
        {
            _registered = true;
            _registeredAt = AnimationManager.Resets;
            AnimationManager.AddTicker(Advance);
        }

        return true;
    }

    /// <summary>Stops where the value is now.</summary>
    public void Stop()
    {
        _active = false;
        _velocity = 0;
    }

    private bool Advance(double seconds)
    {
        if (!_active)
        {
            _registered = false;
            return true;
        }

        var before = _read();
        _apply(before + _velocity * seconds);
        _velocity *= Math.Exp(-Friction * seconds);

        var moved = Math.Abs(_read() - before);
        if (Math.Abs(_velocity) >= MinSpeed && moved >= StuckChange)
        {
            return false;
        }

        _active = false;
        _registered = false;
        _settled?.Invoke();
        return true;
    }
}
