using System;
using Adamantium.UI.Core.Media.Animation;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A ticker that throws is stopped there: the tick goes on to the others, and the next tick runs without it.
/// </summary>
[TestFixture]
public class AnimationTickerTests
{
    [SetUp]
    [TearDown]
    public void ResetAnimations() => AnimationManager.Reset();

    [Test]
    public void ATickerThatThrows_IsStopped_AndTheOthersTickOn()
    {
        var thrown = 0;
        var ticked = 0;
        AnimationManager.AddTicker(_ =>
        {
            thrown++;
            throw new InvalidOperationException("a ticker's own error");
        });
        AnimationManager.AddTicker(_ =>
        {
            ticked++;
            return false;
        });

        Assert.DoesNotThrow(() =>
        {
            AnimationManager.Tick(0.016);
            AnimationManager.Tick(0.016);
        });

        Assert.That((thrown, ticked, AnimationManager.ActiveCount), Is.EqualTo((1, 2, 1)));
    }
}
