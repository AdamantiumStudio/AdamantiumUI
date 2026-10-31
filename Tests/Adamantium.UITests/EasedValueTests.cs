using System;
using Adamantium.UI.Core.Media.Animation;
using NUnit.Framework;

namespace Adamantium.UITests;

public class EasedValueTests
{
    private double _level;

    private static void Settle()
    {
        for (var i = 0; i < 600 && AnimationManager.HasActiveAnimations; i++)
        {
            AnimationManager.Tick(1.0 / 60.0);
        }
    }

    [SetUp]
    public void Reset()
    {
        AnimationManager.Reset();
        _level = 0;
    }

    [Test]
    public void TheEasedValuesOverFramesAndLandsOnTheTarget()
    {
        var settled = 0;
        var ease = new EasedValue(() => _level, level => _level = level, () => settled++);

        ease.EaseTo(2);
        Assert.That(_level, Is.EqualTo(0), "nothing moves before the first frame");

        AnimationManager.Tick(1.0 / 60.0);
        Assert.That(_level, Is.GreaterThan(0).And.LessThan(2), "one frame goes part of the way");

        Settle();
        Assert.Multiple(() =>
        {
            Assert.That(_level, Is.EqualTo(2), "the last step lands exactly on the target");
            Assert.That(ease.IsActive, Is.False);
            Assert.That(settled, Is.EqualTo(1), "settling is reported once");
            Assert.That(AnimationManager.HasActiveAnimations, Is.False, "the ticker is gone once settled");
        });
    }

    [Test]
    public void QuickNotchesAddUpIntoOneTarget()
    {
        var ease = new EasedValue(() => _level, level => _level = level);

        ease.EaseBy(1);
        AnimationManager.Tick(1.0 / 60.0);
        ease.EaseBy(1);

        Assert.That(ease.Target, Is.EqualTo(2), "the second notch adds to where the first was going, not to where it is");
        Settle();
        Assert.That(_level, Is.EqualTo(2));
    }

    [Test]
    public void TheTargetStaysWithinTheLimits()
    {
        var ease = new EasedValue(() => _level, level => _level = level);

        ease.EaseBy(5, -1, 1);
        ease.EaseBy(5, -1, 1);
        Settle();

        Assert.That(_level, Is.EqualTo(1));
    }

    [Test]
    public void StopLeavesTheZoomWhereItIs()
    {
        var ease = new EasedValue(() => _level, level => _level = level);

        ease.EaseTo(3);
        AnimationManager.Tick(1.0 / 60.0);
        var reached = _level;
        ease.Stop();
        Settle();

        Assert.Multiple(() =>
        {
            Assert.That(_level, Is.EqualTo(reached));
            Assert.That(ease.Target, Is.EqualTo(reached), "a stopped ease aims at where the zoom is");
        });
    }

    [Test]
    public void AnEaseStillWorksAfterTheHeartbeatWasReset()
    {
        var ease = new EasedValue(() => _level, level => _level = level);

        ease.EaseTo(1);
        AnimationManager.Reset();
        ease.EaseTo(3);
        Settle();

        Assert.That(_level, Is.EqualTo(3), "the dropped ticker is registered again");
    }

    [Test]
    public void ATargetThatIsNotANumberIsIgnored()
    {
        var ease = new EasedValue(() => _level, level => _level = level);

        ease.EaseTo(double.NaN);
        ease.EaseBy(Math.Log(-1));

        Assert.Multiple(() =>
        {
            Assert.That(ease.IsActive, Is.False);
            Assert.That(AnimationManager.HasActiveAnimations, Is.False);
        });
    }

    [Test]
    public void AZoomTheOwnerHoldsBackComesToRest()
    {
        var limit = 5.0;
        var settled = 0;
        var ease = new EasedValue(() => _level, level => _level = Math.Min(level, limit), () => settled++);

        ease.EaseTo(4);
        AnimationManager.Tick(1.0 / 60.0);
        limit = _level;
        Settle();

        Assert.Multiple(() =>
        {
            Assert.That(ease.IsActive, Is.False, "a step that cannot move ends the ease");
            Assert.That(settled, Is.EqualTo(1));
            Assert.That(AnimationManager.HasActiveAnimations, Is.False);
        });
    }

    [Test]
    public void AZoomSetElsewhereIsWhereTheEaseContinuesFrom()
    {
        var ease = new EasedValue(() => _level, level => _level = level);

        ease.EaseTo(1);
        AnimationManager.Tick(1.0 / 60.0);
        _level = -3;
        AnimationManager.Tick(1.0 / 60.0);

        Assert.That(_level, Is.LessThan(-2), "each step starts from the live zoom the owner reports");
        Assert.That(Math.Abs(ease.Target - 1), Is.LessThan(1e-12));
    }
}
