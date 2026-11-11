using System;
using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Media.Animation;

/// <summary>
/// Keeps a dragged view moving on the animation heartbeat: a flick coasts on with friction after the hand lifts, and a
/// wheel glides smoothly to an accumulating target. The position is whatever the owner pans - a scroll offset, a
/// canvas offset - read and written through the owner, which also clamps it; a step that does not move ends the motion.
/// Given <see cref="Edges"/>, a flick runs out past an edge and turns back on a spring instead, and one started past an
/// edge springs back to it.
/// </summary>
public sealed class PanInertia
{
    private const double SettledDistance = 0.5;
    private const double StuckDistance = 0.01;

    private bool _thrownInX;
    private bool _thrownInY;
    private bool _springingX;
    private bool _springingY;

    private readonly Func<Vector2<double>> _read;
    private readonly Action<Vector2<double>> _apply;
    private readonly Action _settled;
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
    /// <paramref name="apply"/>; <paramref name="settled"/>, when given, is told each time a flick or a glide comes to
    /// rest by itself - not when it is stopped.</summary>
    public PanInertia(Func<Vector2<double>> read, Action<Vector2<double>> apply, Action settled = null)
    {
        _read = read;
        _apply = apply;
        _settled = settled;
    }

    /// <summary>How fast a flick slows, per second: higher stops it sooner, lower coasts further.</summary>
    public double Friction { get; set; } = 6.0;

    /// <summary>How fast a glide closes on its target, per second: higher is snappier, lower glides longer.</summary>
    public double SmoothRate { get; set; } = 14.0;

    /// <summary>Speed, in units per second, below which a flick counts as stopped.</summary>
    public double MinFlickSpeed { get; set; } = 12.0;

    /// <summary>Release speed, in units per second, a flick needs to start.</summary>
    public double FlickStartSpeed { get; set; } = 80.0;

    /// <summary>The position nearest a given one that is inside the edges, or null for none. With edges the owner writes
    /// positions past them as they are: the flick keeps them in.</summary>
    public Func<Vector2<double>, Vector2<double>> Edges { get; set; }

    /// <summary>How far past an edge a flick may run along each axis; 0 holds that axis at its edge, null sets no
    /// limit.</summary>
    public Func<Vector2<double>> FarthestPast { get; set; }

    /// <summary>How stiff the spring that brings a flick back to an edge is, per second; it is critically damped.</summary>
    public double EdgeSpring { get; set; } = 12.0;

    /// <summary>Whether a flick or a glide is still moving.</summary>
    public bool IsActive => _active;

    /// <summary>How fast a flick is moving now, in units per second; zero when no flick runs.</summary>
    public Vector2<double> Velocity => _active && _target is null ? _velocity : default;

    /// <summary>Where a glide is going - the live position when nothing glides. What the next wheel notch adds to.</summary>
    public Vector2<double> Target => _active && _target.HasValue ? _target.Value : _read();

    /// <summary>Glides smoothly to <paramref name="target"/>, already clamped by the owner; <see cref="Edges"/> play no
    /// part in it.</summary>
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
            _trackedVelocity = _trackedVelocity * Flick.KeptOfLast +
                               (current - _lastPosition) * (1.0 / seconds) * (1 - Flick.KeptOfLast);
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
            unchecked(timestamp - _lastTimestamp) > Flick.RestMilliseconds)
        {
            return false;
        }

        Fling(_trackedVelocity);
        return true;
    }

    /// <summary>Flicks on from where the position is at <paramref name="velocity"/>, in units per second. With
    /// <see cref="Edges"/>, a position past an edge springs back to it, at no speed as well.</summary>
    public void Fling(Vector2<double> velocity)
    {
        _velocity = velocity;
        _target = null;
        ForgetEdges();

        if (Edges is { } edges)
        {
            var position = _read();
            var held = edges(position);
            _thrownInX = ThrownIn(position.X - held.X, velocity.X);
            _thrownInY = ThrownIn(position.Y - held.Y, velocity.Y);
        }

        Start();
    }

    /// <summary>Stops where the position is now.</summary>
    public void Stop()
    {
        _active = false;
        _target = null;
        _velocity = default;
        ForgetEdges();
    }

    private void ForgetEdges()
    {
        _thrownInX = false;
        _thrownInY = false;
        _springingX = false;
        _springingY = false;
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

        if (_target is null && Edges is { } edges)
        {
            if (!FlickWithin(edges, seconds)) return false;

            _active = false;
            _registered = false;
            _settled?.Invoke();
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
        _settled?.Invoke();
        return true;
    }

    private bool FlickWithin(Func<Vector2<double>, Vector2<double>> edges, double seconds)
    {
        // One frame, axis by axis: inside it coasts with friction; past an edge a critically damped spring takes over
        // with the speed it brought, so it runs out, turns and comes back in one motion. True at rest.
        try
        {
            var position = _read();
            var held = edges(position);
            var ahead = edges(position + _velocity * seconds);
            var farthest = FarthestPast?.Invoke() ?? new Vector2<double>(double.PositiveInfinity, double.PositiveInfinity);

            var (x, vx, xDone) = Axis(position.X, held.X, ahead.X, _velocity.X, farthest.X, seconds,
                ref _thrownInX, ref _springingX);
            var (y, vy, yDone) = Axis(position.Y, held.Y, ahead.Y, _velocity.Y, farthest.Y, seconds,
                ref _thrownInY, ref _springingY);

            _velocity = new Vector2<double>(vx, vy);
            var next = new Vector2<double>(x, y);
            _apply(next);

            // The owner would not move it: a step that does not move ends the motion, as without edges.
            var stuck = (_read() - position).Length() < StuckDistance && (next - position).Length() >= StuckDistance;
            return stuck || xDone && yDone;
        }
        catch
        {
            // Dropped by the heartbeat on a throw: the next flick must be able to start again.
            _active = false;
            _registered = false;
            throw;
        }
    }

    private (double Position, double Velocity, bool Done) Axis(double position, double edge, double edgeAhead,
        double velocity, double farthest, double seconds, ref bool thrownIn, ref bool springing)
    {
        if (farthest <= 0) return (edge, 0, true);

        var past = position - edge;

        // A throw back in glides as inside while it can still reach the edge; the spring's own way back does not.
        thrownIn = thrownIn && ThrownIn(past, velocity);

        if (past == 0 || thrownIn)
        {
            // On the edge on the spring's way back - put there by something else: it ends there, not coasting in.
            if (past == 0 && springing) return (position, 0, true);

            var moved = position + velocity * seconds;

            // Crossing an edge within the frame: coasting up to it, the spring for the rest of the frame.
            if (past == 0 && velocity != 0 && moved != edgeAhead && Math.Sign(moved - edgeAhead) == Math.Sign(velocity))
            {
                var toEdge = Math.Clamp((edgeAhead - position) / velocity, 0, seconds);
                var atEdge = Cap(velocity * Math.Exp(-Friction * toEdge), farthest);
                var (next, speed) = Spring(0, atEdge, seconds - toEdge);
                springing = true;
                return (edgeAhead + next, speed, false);
            }

            var after = velocity * Math.Exp(-Friction * seconds);
            return (moved, after, !thrownIn && Math.Abs(after) < MinFlickSpeed);
        }

        if (Math.Sign(velocity) == Math.Sign(past)) velocity = Cap(velocity, farthest);

        springing = true;
        var (left, rest) = Spring(past, velocity, seconds);
        if (Math.Abs(left) < SettledDistance && Math.Abs(rest) < MinFlickSpeed) return (edge, 0, true);

        return (edge + left, rest, false);
    }

    private bool ThrownIn(double past, double velocity) =>
        past != 0 && Math.Sign(velocity) != Math.Sign(past) && Math.Abs(velocity) / Friction >= Math.Abs(past);

    private double Cap(double velocity, double farthest)
    {
        // Running outwards no farther than `farthest`: the spring's peak from the edge is v / (e * w).
        var fastest = farthest * EdgeSpring * Math.E;
        return Math.Clamp(velocity, -fastest, fastest);
    }

    private (double Past, double Velocity) Spring(double past, double velocity, double seconds)
    {
        // The critically damped spring toward the edge, stepped exactly.
        var decay = Math.Exp(-EdgeSpring * seconds);
        var lead = velocity + EdgeSpring * past;
        return ((past + lead * seconds) * decay, (velocity - EdgeSpring * lead * seconds) * decay);
    }
}
