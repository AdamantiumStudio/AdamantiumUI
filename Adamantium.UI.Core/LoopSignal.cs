using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;

namespace Adamantium.UI.Core;

/// <summary>The update loop's single channel, shared with the dispatcher: posted actions and deduplicated wake tokens. The
/// loop blocks on it between frames, so an idle window costs nothing; animations are paced by frames instead.</summary>
public static class LoopSignal
{
    // Single reader: the loop thread. Unbounded: a posted action must never be dropped.
    private static readonly Channel<Action> Pipe =
        Channel.CreateUnbounded<Action>(new UnboundedChannelOptions { SingleReader = true });

    /// <summary>The "there is work" token. Carries no action - reading it IS the wake.</summary>
    private static readonly Action WakeToken = static () => { };

    private static int _wakePending;   // 0/1 - at most one wake token is ever queued at a time

    private static readonly ConcurrentQueue<Action> Awaited = new();
    private static readonly ManualResetEventSlim AwaitedArrived = new(false, 0);

    private static int _posted;
    private static long _requests;

    /// <summary>True while a posted action waits for the loop.</summary>
    public static bool HasPostedWork => Volatile.Read(ref _posted) > 0;

    /// <summary>How many times the loop has been asked for a frame or handed work so far; a number that moved since a
    /// moment means something asked after it.</summary>
    public static long Requests => Interlocked.Read(ref _requests);

    /// <summary>Queues work to run on the loop thread (drained at the start of the next frame). Its arrival is itself a wake.</summary>
    public static void Post(Action action)
    {
        Interlocked.Increment(ref _requests);
        Interlocked.Increment(ref _posted);
        if (!Pipe.Writer.TryWrite(action))
        {
            Interlocked.Decrement(ref _posted);
        }
    }

    /// <summary>Queues work another thread is waiting on - a question from an accessibility client: it runs the moment the
    /// loop is between frames, not at the start of the next one. A frame follows it.</summary>
    public static void PostAwaited(Action action)
    {
        Interlocked.Increment(ref _posted);
        Awaited.Enqueue(action);
        AwaitedArrived.Set();
        Request();
    }

    /// <summary>Holds the loop thread between frames for <paramref name="milliseconds"/>, running awaited work as it
    /// arrives.</summary>
    public static void Pause(double milliseconds, CancellationToken token)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(milliseconds * Stopwatch.Frequency / 1000);
        try
        {
            for (var left = milliseconds; left >= 1.0; left = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), deadline).TotalMilliseconds)
            {
                if (!AwaitedArrived.Wait((int)left, token))
                {
                    return;
                }

                AwaitedArrived.Reset();
                RunAwaited();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Something changed - the loop owes another frame. Idempotent and near-free; safe from any thread.</summary>
    public static void Request()
    {
        Interlocked.Increment(ref _requests);
        if (Interlocked.Exchange(ref _wakePending, 1) == 0)
        {
            Pipe.Writer.TryWrite(WakeToken);
        }
    }

    /// <summary>Runs every posted action in order, on the loop thread, at the start of a frame. Clears the wake token FIRST, so
    /// anything signaled while this frame runs queues a fresh one and becomes the wake for the next frame - never swallowed.</summary>
    public static void Drain()
    {
        Interlocked.Exchange(ref _wakePending, 0);
        RunAwaited();
        while (Pipe.Reader.TryRead(out var action))
        {
            if (ReferenceEquals(action, WakeToken)) continue;
            Interlocked.Decrement(ref _posted);
            try { action(); }
            catch (Exception ex) { Console.WriteLine(ex); }
        }
    }

    private static void RunAwaited()
    {
        while (Awaited.TryDequeue(out var action))
        {
            Interlocked.Decrement(ref _posted);
            try { action(); }
            catch (Exception ex) { Console.WriteLine(ex); }
        }
    }

    /// <summary>Blocks the loop thread until something is in the pipe, or <paramref name="timeoutMs"/> elapses. The timeout is a
    /// safety net, not a schedule: every source that needs a frame writes here, so it only bounds the damage of one nobody
    /// thought of - a late frame rather than a frozen window.</summary>
    public static void Wait(int timeoutMs, CancellationToken token)
    {
        try
        {
            var pending = Pipe.Reader.WaitToReadAsync(token);
            if (pending.IsCompleted)
            {
                pending.GetAwaiter().GetResult();   // observe the result; the items stay in the pipe for Drain
                return;
            }
            // Only allocates when the loop actually goes to sleep - i.e. when the UI is idle, which is exactly when nobody cares.
            pending.AsTask().Wait(timeoutMs, token);
        }
        catch (OperationCanceledException) { /* shutting down */ }
        catch (AggregateException) { /* the wait was canceled */ }
    }
}
