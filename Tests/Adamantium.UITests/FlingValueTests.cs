using Adamantium.Mathematics;
using Adamantium.UI.Core.Media.Animation;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A thrown number coasts with friction and says when it rests; a pan's flick says so too.</summary>
public class FlingValueTests
{
    private static void Frames(int count)
    {
        for (var i = 0; i < count; i++) AnimationManager.Tick(1 / 60.0);
    }

    [Test]
    public void AThrownValueCoastsSlowsAndSaysWhenItRests()
    {
        var value = 0.0;
        var rested = 0;
        var fling = new FlingValue(() => value, v => value = v, () => rested++);

        for (uint at = 0; at <= 50; at += 10)
        {
            value = at / 10.0;
            fling.Track(at);
        }

        Assert.That(fling.Release(50), Is.True, "a hundred a second is a throw");

        Frames(1);
        var first = value - 5;
        Frames(1);
        var second = value - 5 - first;

        Frames(600);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.GreaterThan(0));
            Assert.That(second, Is.LessThan(first), "friction slows it");
            Assert.That(fling.IsActive, Is.False);
            Assert.That(rested, Is.EqualTo(1));
        });
    }

    [Test]
    public void AStoppedFlingDoesNotSayItRested()
    {
        var value = 0.0;
        var rested = 0;
        var fling = new FlingValue(() => value, v => value = v, () => rested++);

        fling.Track(0);
        value = 1;
        fling.Track(10);
        fling.Release(10);
        fling.Stop();
        Frames(30);

        Assert.That(rested, Is.Zero);
    }

    [Test]
    public void ASlowReleaseDoesNotCoast()
    {
        var value = 0.0;
        var fling = new FlingValue(() => value, v => value = v);

        fling.Track(0);
        value = 0.001;
        fling.Track(10);

        Assert.That(fling.Release(10), Is.False);
    }

    [Test]
    public void APanFlickSaysWhenItRests()
    {
        var at = Vector2<double>.Zero;
        var rested = 0;
        var pan = new PanInertia(() => at, v => at = v, () => rested++);

        for (uint t = 0; t <= 50; t += 10)
        {
            at = new Vector2<double>(t * 10, 0);
            pan.Track(t);
        }

        Assert.That(pan.Release(50), Is.True);
        Frames(600);

        Assert.That(rested, Is.EqualTo(1));
    }
}
