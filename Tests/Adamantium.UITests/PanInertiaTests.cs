using System;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Media.Animation;
using NUnit.Framework;

namespace Adamantium.UITests;

public class PanInertiaTests
{
    private Vector2<double> _position;
    private double _limit;

    private static void Settle()
    {
        for (var i = 0; i < 600 && AnimationManager.HasActiveAnimations; i++)
        {
            AnimationManager.Tick(1.0 / 60.0);
        }
    }

    private PanInertia Make() => new(() => _position,
        position => _position = new Vector2<double>(Math.Min(position.X, _limit), position.Y));

    private void Drag(PanInertia inertia, double pixelsPerMove, uint startTime, int moves)
    {
        inertia.ResetTracking();
        inertia.Track(startTime);
        for (var i = 1; i <= moves; i++)
        {
            _position += new Vector2<double>(pixelsPerMove, 0);
            inertia.Track(startTime + (uint)(i * 10));
        }
    }

    [SetUp]
    public void Reset()
    {
        AnimationManager.Reset();
        _position = Vector2<double>.Zero;
        _limit = double.PositiveInfinity;
    }

    [Test]
    public void AFastLiftCoastsOnAndSlowsToAStop()
    {
        var inertia = Make();
        Drag(inertia, 10, 1000, 5);
        var lifted = _position.X;

        Assert.That(inertia.Release(1050), Is.True, "1000 px/s at the lift is a flick");
        Settle();

        Assert.Multiple(() =>
        {
            Assert.That(_position.X, Is.GreaterThan(lifted + 50), "the content keeps moving the way it was dragged");
            Assert.That(inertia.IsActive, Is.False, "friction brings it to rest");
            Assert.That(AnimationManager.HasActiveAnimations, Is.False);
        });
    }

    [Test]
    public void ALiftAfterTheHandStoppedDoesNotFlick()
    {
        var inertia = Make();
        Drag(inertia, 10, 1000, 5);

        Assert.That(inertia.Release(1050 + 200), Is.False, "the speed before a pause is old news at the lift");
        Assert.That(AnimationManager.HasActiveAnimations, Is.False);
    }

    [Test]
    public void ASlowDragDoesNotFlick()
    {
        var inertia = Make();
        Drag(inertia, 0.5, 1000, 5);

        Assert.That(inertia.Release(1050), Is.False, "50 px/s is below the start speed");
    }

    [Test]
    public void AFlickStopsAtTheEdgeTheOwnerKeeps()
    {
        var inertia = Make();
        Drag(inertia, 10, 1000, 5);
        _limit = _position.X + 20;

        inertia.Release(1050);
        Settle();

        Assert.Multiple(() =>
        {
            Assert.That(_position.X, Is.EqualTo(_limit), "the owner's clamp holds");
            Assert.That(inertia.IsActive, Is.False, "a step that no longer moves ends the flick");
        });
    }

    [Test]
    public void GlidesAddUpAndLandOnTheTarget()
    {
        var inertia = Make();

        inertia.GlideTo(inertia.Target + new Vector2<double>(0, 100));
        Assert.That(_position.Y, Is.EqualTo(0), "a glide does not jump");
        AnimationManager.Tick(1.0 / 60.0);
        inertia.GlideTo(inertia.Target + new Vector2<double>(0, 100));

        Assert.That(inertia.Target.Y, Is.EqualTo(200), "the second notch adds to where the first was going");
        Settle();
        Assert.That(_position.Y, Is.EqualTo(200));
    }

    [Test]
    public void AGlideStillWorksAfterTheHeartbeatWasReset()
    {
        var inertia = Make();

        inertia.GlideTo(new Vector2<double>(0, 50));
        AnimationManager.Reset();
        inertia.GlideTo(new Vector2<double>(0, 100));
        Settle();

        Assert.That(_position.Y, Is.EqualTo(100), "the dropped ticker is registered again");
    }

    [Test]
    public void APressStopsAFlick()
    {
        var inertia = Make();
        Drag(inertia, 10, 1000, 5);
        inertia.Release(1050);
        AnimationManager.Tick(1.0 / 60.0);
        var caught = _position;

        inertia.Stop();
        Settle();

        Assert.That(_position, Is.EqualTo(caught));
    }

    // An edge at x = 100 that a flick may run 20 past; y has no edges.
    private PanInertia WithEdges(Action settled = null) => new(() => _position, position => _position = position, settled)
    {
        Edges = position => new Vector2<double>(Math.Min(position.X, 100), position.Y),
        FarthestPast = () => new Vector2<double>(20, 20)
    };

    [Test]
    public void AFlickIntoAnEdgeRunsOutNoFartherThanAllowedAndComesBack()
    {
        var settled = 0;
        var inertia = WithEdges(() => settled++);
        _position = new Vector2<double>(90, 0);

        inertia.Fling(new Vector2<double>(5000, 0));
        var farthest = 0.0;
        var turnedAt = -1;
        for (var i = 0; i < 120; i++)
        {
            AnimationManager.Tick(1.0 / 60.0);
            if (_position.X > farthest) farthest = _position.X;
            else if (turnedAt < 0) turnedAt = i;
        }

        Assert.Multiple(() =>
        {
            Assert.That(farthest, Is.GreaterThan(105).And.LessThanOrEqualTo(120 + 1e-9), "out past the edge, no farther");
            Assert.That(turnedAt, Is.LessThan(12), "turned within a fifth of a second");
            Assert.That(_position.X, Is.EqualTo(100), "back at the edge");
            Assert.That(inertia.IsActive, Is.False);
            Assert.That(settled, Is.EqualTo(1));
        });
    }

    [Test]
    public void AFlingAtNoSpeedFromPastAnEdgeSpringsBackToIt()
    {
        var inertia = WithEdges();
        _position = new Vector2<double>(160, 7);

        inertia.Fling(Vector2<double>.Zero);
        AnimationManager.Tick(1.0 / 60.0);
        var first = _position.X;
        Settle();

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.LessThan(160).And.GreaterThan(100), "on its way back");
            Assert.That(_position, Is.EqualTo(new Vector2<double>(100, 7)), "at the edge, the free axis left alone");
        });
    }

    [Test]
    public void AnAxisAllowedNoRunIsHeldAtItsEdge()
    {
        var inertia = WithEdges();
        inertia.FarthestPast = () => new Vector2<double>(20, 0);
        _position = new Vector2<double>(0, 0);

        inertia.Fling(new Vector2<double>(300, 900));
        Settle();

        Assert.That(_position.Y, Is.EqualTo(0));
        Assert.That(_position.X, Is.GreaterThan(0), "the other axis coasted");
    }

    [Test]
    public void AThrowBackInFromPastAnEdgeGlidesOnInside()
    {
        var inertia = WithEdges();
        _position = new Vector2<double>(110, 0);

        inertia.Fling(new Vector2<double>(-2000, 0));
        Settle();

        // On the spring it would cross slowed to about -140; glided in at its own speed, it goes on to about -213.
        Assert.That(_position.X, Is.LessThan(-150), "it crossed the edge at its own speed and coasted well in");
    }

    [Test]
    public void ASpringBackSettlesOnce()
    {
        var settled = 0;
        var inertia = WithEdges(() => settled++);
        _position = new Vector2<double>(150, 0);

        inertia.Fling(Vector2<double>.Zero);
        Settle();

        Assert.That(settled, Is.EqualTo(1));
    }

    [Test]
    public void AnEdgeFlickStoppedTellsNothingAndCanBeFlungAgain()
    {
        var settled = 0;
        var inertia = WithEdges(() => settled++);
        _position = new Vector2<double>(150, 0);

        inertia.Fling(Vector2<double>.Zero);
        AnimationManager.Tick(1.0 / 60.0);
        inertia.Stop();
        Settle();
        Assert.That(settled, Is.EqualTo(0), "stopped, not come to rest");

        inertia.Fling(Vector2<double>.Zero);
        Settle();
        Assert.Multiple(() =>
        {
            Assert.That(_position.X, Is.EqualTo(100));
            Assert.That(settled, Is.EqualTo(1));
        });
    }

    [Test]
    public void PutOnTheEdgeMidSpringBackItEndsThereRatherThanCoastingIn()
    {
        var inertia = WithEdges();
        _position = new Vector2<double>(160, 0);

        inertia.Fling(Vector2<double>.Zero);
        for (var i = 0; i < 4; i++) AnimationManager.Tick(1.0 / 60.0);
        _position = new Vector2<double>(100, 0);   // what a clamped write by the owner does
        Settle();

        Assert.That(_position.X, Is.EqualTo(100));
    }

    [Test]
    public void AnEdgeFlickWhoseOwnerThrowsCanStartAgain()
    {
        var throws = true;
        var inertia = new PanInertia(() => _position, position =>
        {
            if (throws) throw new InvalidOperationException("owner");
            _position = position;
        })
        {
            Edges = position => new Vector2<double>(Math.Min(position.X, 100), position.Y),
            FarthestPast = () => new Vector2<double>(20, 20)
        };
        _position = new Vector2<double>(150, 0);

        inertia.Fling(Vector2<double>.Zero);
        try
        {
            AnimationManager.Tick(1.0 / 60.0);   // the heartbeat drops a ticker that throws
        }
        catch (InvalidOperationException)
        {
        }

        Assert.That(_position.X, Is.EqualTo(150), "the throwing step moved nothing");
        throws = false;
        inertia.Fling(Vector2<double>.Zero);
        Settle();

        Assert.That(_position.X, Is.EqualTo(100), "a ticker was registered again");
    }

    [Test]
    public void AStepTheOwnerWillNotTakeEndsAnEdgeFlick()
    {
        var inertia = new PanInertia(() => _position, _ => { })
        {
            Edges = position => new Vector2<double>(Math.Min(position.X, 100), position.Y),
            FarthestPast = () => new Vector2<double>(20, 20)
        };
        _position = new Vector2<double>(150, 0);

        inertia.Fling(Vector2<double>.Zero);
        AnimationManager.Tick(1.0 / 60.0);

        Assert.That(inertia.IsActive, Is.False);
    }
}
