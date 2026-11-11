using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.UI.Core;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>
/// Work another thread waits on, and work posted between frames, is run while the loop holds between frames, not a frame
/// later; ordinary posted work keeps waiting for the frame.
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

    // An idle loop sleeps out its timeout again and again: a sleep must not cancel the one before, which threw on the
    // thread pool every time.
    [Test]
    public void SleepsThatTimeOut_ThrowNothing()
    {
        var thrown = 0;
        EventHandler<System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs> count = (_, e) =>
        {
            if (e.Exception is OperationCanceledException) Interlocked.Increment(ref thrown);
        };
        AppDomain.CurrentDomain.FirstChanceException += count;
        try
        {
            LoopSignal.Wait(20, CancellationToken.None);
            LoopSignal.Wait(20, CancellationToken.None);
            LoopSignal.Wait(20, CancellationToken.None);
            Thread.Sleep(100);
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= count;
        }

        Assert.That(thrown, Is.Zero);
    }

    [Test]
    public void APost_WakesASleepKeptFromBefore()
    {
        LoopSignal.Wait(20, CancellationToken.None);
        var clock = Stopwatch.StartNew();
        var poster = Task.Run(() =>
        {
            Thread.Sleep(50);
            LoopSignal.Post(() => { });
        });

        LoopSignal.Wait(5000, CancellationToken.None);
        var woke = clock.ElapsedMilliseconds;
        poster.Wait();
        LoopSignal.Drain();

        Assert.That(woke, Is.LessThan(2000));
    }

    [Test]
    public void ATimer_TicksOnTheLoop_OneTickWaitingAtATime()
    {
        var loop = Thread.CurrentThread;
        Thread tickedOn = null;
        var ticks = 0;
        var timer = new Adamantium.UI.Core.Dispatcher.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (_, _) =>
        {
            tickedOn = Thread.CurrentThread;
            ticks++;
        };

        LoopSignal.Claim();
        try
        {
            timer.Start();
            Thread.Sleep(1000);
            Assert.That(tickedOn, Is.Null, "not on the timer's own thread");

            timer.Interval = TimeSpan.FromHours(1);
            LoopSignal.Drain();
            timer.Stop();
        }
        finally
        {
            LoopSignal.Unclaim();
            timer.Dispose();
        }

        Assert.That(tickedOn, Is.SameAs(loop));
        Assert.That(ticks, Is.EqualTo(1), "the ticks of a stalled loop are not run one after another");
    }

    [Test]
    public void ATickWaitingFromBeforeARestart_IsDropped()
    {
        var ticks = 0;
        var timer = new Adamantium.UI.Core.Dispatcher.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(5) };
        timer.Tick += (_, _) => ticks++;

        LoopSignal.Claim();
        try
        {
            timer.Start();
            SpinWait.SpinUntil(() => LoopSignal.HasPostedWork, 2000);
            timer.Stop();
            timer.Start(TimeSpan.FromSeconds(10));

            LoopSignal.Drain();
            timer.Stop();
        }
        finally
        {
            LoopSignal.Unclaim();
            timer.Dispose();
        }

        Assert.That(ticks, Is.Zero, "a restart waits its own delay");
    }

    [Test]
    public void WorkBetweenFrames_RunsDuringThePause_AskingForNoFrame()
    {
        var loop = Thread.CurrentThread;
        Thread ranOn = null;
        double ranAt = -1;
        var frames = LoopSignal.FrameRequests;
        var clock = Stopwatch.StartNew();
        var poster = Task.Run(() =>
        {
            Thread.Sleep(50);
            LoopSignal.PostBetweenFrames(() =>
            {
                ranOn = Thread.CurrentThread;
                ranAt = clock.Elapsed.TotalMilliseconds;
            });
        });

        LoopSignal.Pause(2000, CancellationToken.None);
        poster.Wait();

        Assert.Multiple(() =>
        {
            Assert.That(ranOn, Is.SameAs(loop));
            Assert.That(ranAt, Is.GreaterThan(0).And.LessThan(1000));
            Assert.That(LoopSignal.HasPostedWork, Is.False);
            Assert.That(LoopSignal.FrameRequests, Is.EqualTo(frames));
        });
    }

    [Test]
    public void WorkBetweenFrames_WakesAnIdleLoop()
    {
        var ran = false;
        var frames = LoopSignal.FrameRequests;
        LoopSignal.Wait(20, CancellationToken.None);
        var clock = Stopwatch.StartNew();
        var poster = Task.Run(() =>
        {
            Thread.Sleep(50);
            LoopSignal.PostBetweenFrames(() => ran = true);
        });

        LoopSignal.Wait(5000, CancellationToken.None);
        var woke = clock.ElapsedMilliseconds;
        poster.Wait();
        LoopSignal.Drain();

        Assert.Multiple(() =>
        {
            Assert.That(woke, Is.LessThan(2000));
            Assert.That(ran, Is.True);
            Assert.That(LoopSignal.HasPostedWork, Is.False);
            Assert.That(LoopSignal.FrameRequests, Is.EqualTo(frames));
        });
    }

    // Work that posts more while the frame drains it - Chromium asking for its next turn - must still wake the loop.
    [Test]
    public void WorkPostedAgainWhileDrained_WakesTheLoopAfter()
    {
        var runs = 0;
        void Turn()
        {
            if (++runs < 2) LoopSignal.PostBetweenFrames(Turn);
        }

        LoopSignal.PostBetweenFrames(Turn);
        LoopSignal.Drain();
        Assert.That(runs, Is.EqualTo(1), "what the work posts again is left for the pause");

        var clock = Stopwatch.StartNew();
        LoopSignal.Wait(5000, CancellationToken.None);
        var wokeForTheLeftWork = clock.ElapsedMilliseconds;
        LoopSignal.Drain();

        // The wake is not left marked as pending with no token behind it: the next post from another thread wakes too.
        clock.Restart();
        var later = Task.Run(() =>
        {
            Thread.Sleep(50);
            LoopSignal.PostBetweenFrames(() => { });
        });
        LoopSignal.Wait(5000, CancellationToken.None);
        var wokeForTheNext = clock.ElapsedMilliseconds;
        later.Wait();
        LoopSignal.Drain();

        Assert.Multiple(() =>
        {
            Assert.That(runs, Is.EqualTo(2));
            Assert.That(wokeForTheLeftWork, Is.LessThan(100));
            Assert.That(wokeForTheNext, Is.LessThan(200));
        });
    }

    [Test]
    public void WorkThatKeepsPostingMore_DoesNotHoldThePausePastItsEnd()
    {
        var keepGoing = true;
        void Turn()
        {
            Thread.Sleep(1);
            if (keepGoing) LoopSignal.PostBetweenFrames(Turn);
        }

        LoopSignal.PostBetweenFrames(Turn);
        var clock = Stopwatch.StartNew();
        LoopSignal.Pause(50, CancellationToken.None);
        var paused = clock.ElapsedMilliseconds;
        keepGoing = false;
        LoopSignal.Drain();
        LoopSignal.Drain();

        Assert.That(paused, Is.LessThan(500));
    }

    // What posted work makes another loop ask for runs in the same frame, after it, as before.
    [Test]
    public void WorkBetweenFramesPostedByPostedWork_RunsInTheSameDrainAfterIt()
    {
        var order = "";
        LoopSignal.Post(() =>
        {
            order += "posted ";
            LoopSignal.PostBetweenFrames(() => order += "between");
        });

        LoopSignal.Drain();

        Assert.That(order, Is.EqualTo("posted between"));
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
