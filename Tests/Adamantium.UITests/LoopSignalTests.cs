using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.UI.Core;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>
/// Work another thread waits on is run while the loop holds between frames, not a frame later; ordinary posted work keeps
/// waiting for the frame.
/// </summary>
[TestFixture]
public class LoopSignalTests
{
    [SetUp]
    public void Empty() => LoopSignal.Drain();

    [Test]
    public void AwaitedWork_RunsDuringThePause_OnTheLoopThread()
    {
        var loop = Thread.CurrentThread;
        Thread ranOn = null;
        double ranAt = -1;
        var clock = Stopwatch.StartNew();
        var asker = Task.Run(() =>
        {
            Thread.Sleep(50);
            LoopSignal.PostAwaited(() =>
            {
                ranOn = Thread.CurrentThread;
                ranAt = clock.Elapsed.TotalMilliseconds;
            });
        });

        LoopSignal.Pause(2000, CancellationToken.None);
        asker.Wait();

        Assert.That(ranOn, Is.SameAs(loop));
        Assert.That(ranAt, Is.GreaterThan(0).And.LessThan(1000));
        Assert.That(LoopSignal.HasPostedWork, Is.False);
    }

    [Test]
    public void PostedWork_WaitsForTheFrame()
    {
        var ran = false;
        LoopSignal.Post(() => ran = true);

        LoopSignal.Pause(30, CancellationToken.None);
        Assert.That(ran, Is.False);
        Assert.That(LoopSignal.HasPostedWork, Is.True);

        LoopSignal.Drain();
        Assert.That(ran, Is.True);
    }

    [Test]
    public void AwaitedWork_LeftForTheFrame_IsRunByIt()
    {
        var ran = false;
        LoopSignal.PostAwaited(() => ran = true);

        LoopSignal.Drain();

        Assert.That(ran, Is.True);
        Assert.That(LoopSignal.HasPostedWork, Is.False);
    }
}
