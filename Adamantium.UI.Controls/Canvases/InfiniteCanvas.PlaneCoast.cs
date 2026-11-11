using System;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Media.Animation;

namespace Adamantium.UI.Controls.Canvases;

public partial class InfiniteCanvas
{
    private const double EdgeSpring = 12.0;
    private const double FarthestPast = 0.12;

    private Vector2<double> _planeCoastVelocity;
    private bool _thrownInAcross;
    private bool _thrownInDown;
    private bool _planeCoasting;
    private bool _planeCoastTicking;
    private long _planeCoastTickingAt;

    private void CoastOnPlane(Vector2<double> velocity)
    {
        // `velocity` is the hand's: past an edge the band showed the plane moving slower, and it goes on at that speed.
        var held = ClampToPlane(Offset, Scale);
        var room = Room(Scale, out _, out _);
        _planeCoastVelocity = new Vector2<double>(
            velocity.X * BandSlope(_handOffset.X - held.X, Offset.X - held.X, room.Width),
            velocity.Y * BandSlope(_handOffset.Y - held.Y, Offset.Y - held.Y, room.Height));
        _thrownInAcross = ThrownIn(Offset.X - held.X, _planeCoastVelocity.X);
        _thrownInDown = ThrownIn(Offset.Y - held.Y, _planeCoastVelocity.Y);
        _planeCoasting = true;
        if (_planeCoastTicking && _planeCoastTickingAt == AnimationManager.Resets) return;

        _planeCoastTicking = true;
        _planeCoastTickingAt = AnimationManager.Resets;
        AnimationManager.AddTicker(AdvancePlaneCoast);
    }

    private bool ThrownIn(double past, double velocity) =>
        past != 0 && Math.Sign(velocity) != Math.Sign(past) && Math.Abs(velocity) / _coast.Friction >= Math.Abs(past);

    private static double BandSlope(double handPast, double shownPast, double span)
    {
        // How much of the hand's move the band shows `handPast` past the edge; all of it on the plane.
        if (shownPast == 0 || span <= 0) return 1;

        var stretch = Math.Abs(handPast) * BandStiffness / span + 1;
        return BandStiffness / (stretch * stretch);
    }

    private void StopPlaneCoast()
    {
        _planeCoasting = false;
        _planeCoastVelocity = default;
    }

    private bool AdvancePlaneCoast(double seconds)
    {
        if (!_planeCoasting || !HasPlaneBounds)
        {
            _planeCoasting = false;
            _planeCoastTicking = false;
            return true;
        }

        try
        {
            return StepPlaneCoast(seconds);
        }
        catch
        {
            // The ticker is dropped on a throw; the next flick must be able to start one.
            _planeCoasting = false;
            _planeCoastTicking = false;
            throw;
        }
    }

    private bool StepPlaneCoast(double seconds)
    {
        // Axis by axis: on the plane it coasts with friction; past an edge a critically damped spring takes over with
        // the speed it brought, so the plane runs out, turns and comes back in one motion.
        var offset = Offset;
        var held = ClampToPlane(offset, Scale);
        var ahead = ClampToPlane(offset + _planeCoastVelocity * seconds, Scale);
        var room = Room(Scale, out _, out _);
        var bounds = PlaneBounds;

        var (x, vx, xDone) = CoastAxis(offset.X, held.X, ahead.X, _planeCoastVelocity.X, room.Width,
            bounds.Width * Scale > room.Width, seconds, ref _thrownInAcross);
        var (y, vy, yDone) = CoastAxis(offset.Y, held.Y, ahead.Y, _planeCoastVelocity.Y, room.Height,
            bounds.Height * Scale > room.Height, seconds, ref _thrownInDown);

        _planeCoastVelocity = new Vector2<double>(vx, vy);
        StretchTo(new Vector2<double>(x, y));

        if (!xDone || !yDone) return false;

        _planeCoasting = false;
        _planeCoastTicking = false;
        RememberLayout();
        return true;
    }

    private (double Position, double Velocity, bool Done) CoastAxis(double position, double edge, double edgeAhead,
        double velocity, double span, bool overflows, double seconds, ref bool thrownIn)
    {
        if (!overflows) return (edge, 0, true);

        var friction = _coast.Friction;
        var past = position - edge;

        // A throw back in glides as on the plane while it can still reach it; the spring's own way back does not.
        thrownIn = thrownIn && ThrownIn(past, velocity);
        var inwards = thrownIn;

        if (past == 0 || inwards)
        {
            var moved = position + velocity * seconds;
            var after = velocity * Math.Exp(-friction * seconds);

            // Crossing an edge within the frame: coasting up to it, the spring for the rest of the frame.
            if (past == 0 && velocity != 0 && moved != edgeAhead && Math.Sign(moved - edgeAhead) == Math.Sign(velocity))
            {
                var toEdge = Math.Clamp((edgeAhead - position) / velocity, 0, seconds);
                var atEdge = Cap(velocity * Math.Exp(-friction * toEdge), span);
                var (next, speed) = Spring(0, atEdge, seconds - toEdge);
                return (edgeAhead + next, speed, false);
            }

            return (moved, after, !inwards && Math.Abs(after) < _coast.MinFlickSpeed);
        }

        if (Math.Sign(velocity) == Math.Sign(past)) velocity = Cap(velocity, span);

        var (left, rest) = Spring(past, velocity, seconds);
        if (Math.Abs(left) < 0.5 && Math.Abs(rest) < _coast.MinFlickSpeed) return (edge, 0, true);

        return (edge + left, rest, false);
    }

    private static double Cap(double velocity, double span)
    {
        // Running outwards: no farther past than a share of the viewport - the spring's peak is v / (e * w).
        var fastest = FarthestPast * span * EdgeSpring * Math.E;
        return Math.Clamp(velocity, -fastest, fastest);
    }

    private static (double Past, double Velocity) Spring(double past, double velocity, double seconds)
    {
        // The critically damped spring toward the edge, stepped exactly.
        var decay = Math.Exp(-EdgeSpring * seconds);
        var lead = velocity + EdgeSpring * past;
        return ((past + lead * seconds) * decay, (velocity - EdgeSpring * lead * seconds) * decay);
    }
}
