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
}
