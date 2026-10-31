using System;
using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Media.Animation;

/// <summary>
/// Keeps a dragged view moving on the animation heartbeat: a flick coasts on with friction after the hand lifts, and a
/// wheel glides smoothly to an accumulating target. The position is whatever the owner pans - a scroll offset, a
/// canvas offset - read and written through the owner, which also clamps it; a step that does not move ends the motion.
/// </summary>
public sealed class PanInertia
{
    private const uint FlickRestMilliseconds = 100;
    private const double SettledDistance = 0.5;
    private const double StuckDistance = 0.01;

    private readonly Func<Vector2<double>> _read;
    private readonly Action<Vector2<double>> _apply;
    private Vector2<double>? _target;
    private Vector2<double> _velocity;
    private Vector2<double> _trackedVelocity;
    private Vector2<double> _lastPosition;
    private uint _lastTimestamp;
    private bool _hasLastMove;
    private bool _active;
    private bool _registered;
    private long _registeredAt;

    /// <summary>Makes inertia that reads the panned position with <paramref name="read"/> and moves it with
    /// <paramref name="apply"/>.</summary>
    public PanInertia(Func<Vector2<double>> read, Action<Vector2<double>> apply)
    {
        _read = read;
        _apply = apply;
    }

    /// <summary>How fast a flick slows, per second: higher stops it sooner, lower coasts further.</summary>
    public double Friction { get; set; } = 6.0;

    /// <summary>How fast a glide closes on its target, per second: higher is snappier, lower glides longer.</summary>
    public double SmoothRate { get; set; } = 14.0;

    /// <summary>Speed, in units per second, below which a flick counts as stopped.</summary>
    public double MinFlickSpeed { get; set; } = 12.0;

    /// <summary>Release speed, in units per second, a flick needs to start.</summary>
    public double FlickStartSpeed { get; set; } = 80.0;

    /// <summary>Whether a flick or a glide is still moving.</summary>
    public bool IsActive => _active;

    /// <summary>Where a glide is going - the live position when nothing glides. What the next wheel notch adds to.</summary>
    public Vector2<double> Target => _active && _target.HasValue ? _target.Value : _read();

    /// <summary>Glides smoothly to <paramref name="target"/>, already clamped by the owner.</summary>
    public void GlideTo(Vector2<double> target)
    {
        _target = target;
        _velocity = default;
        Start();
    }

    /// <summary>Forgets the moves seen so far - a new press starts a new drag.</summary>
    public void ResetTracking()
    {
        _hasLastMove = false;
        _trackedVelocity = default;
    }

    /// <summary>Notes where the drag has moved the position, at the time the hand made the move, in milliseconds. Timed
    /// by the moves rather than by frames, so a frame's worth of moves handled at once does not read as a fling.</summary>
    public void Track(uint timestamp)
    {
        var current = _read();
        if (_hasLastMove && timestamp != _lastTimestamp)
        {
            var seconds = unchecked(timestamp - _lastTimestamp) / 1000.0;
            _trackedVelocity = _trackedVelocity * 0.4 + (current - _lastPosition) * (1.0 / seconds) * 0.6;
        }

        _hasLastMove = true;
        _lastTimestamp = timestamp;
        _lastPosition = current;
    }

    /// <summary>The hand lifted at <paramref name="timestamp"/>: flicks on if it was still moving fast; one that had
    /// already stopped does not. True when a flick started.</summary>
    public bool Release(uint timestamp)
    {
        if (!_hasLastMove || _trackedVelocity.Length() <= FlickStartSpeed ||
            unchecked(timestamp - _lastTimestamp) > FlickRestMilliseconds)
        {
            return false;
        }

        _velocity = _trackedVelocity;
        _target = null;
        Start();
        return true;
    }

    /// <summary>Stops where the position is now.</summary>
    public void Stop()
    {
        _active = false;
        _target = null;
        _velocity = default;
    }

    private void Start()
    {
        _active = true;
        if (_registered && _registeredAt == AnimationManager.Resets)
        {
            return;
        }

        _registered = true;
        _registeredAt = AnimationManager.Resets;
        AnimationManager.AddTicker(Advance);
    }

    private bool Advance(double seconds)
    {
        if (!_active)
        {
            _registered = false;
            return true;
        }

        var before = _read();
        Vector2<double> next;
        if (_target is { } target)
        {
            next = before + (target - before) * (1.0 - Math.Exp(-SmoothRate * seconds));
            if ((target - next).Length() < SettledDistance)
            {
                next = target;
            }
        }
        else
        {
            next = before + _velocity * seconds;
            _velocity *= Math.Exp(-Friction * seconds);
        }

        _apply(next);
        var after = _read();
        var moved = (after - before).Length();

        var done = _target is { } goal
            ? (goal - after).Length() < SettledDistance || moved < StuckDistance
            : _velocity.Length() < MinFlickSpeed || moved < StuckDistance;
        if (!done)
        {
            return false;
        }

        _active = false;
        _registered = false;
        return true;
    }
}
