using System;
using System.Threading;

namespace Adamantium.UI.Core;

/// <summary>Wakes the render thread, which sleeps between frames, for what it draws without the loop.</summary>
public static class RenderSignal
{
    private static readonly SemaphoreSlim Woken = new(0, 1);
    private static int _pending;
    private static int _renderThreads;

    /// <summary>A render thread starts waiting on the signal; without one, a wake goes to the loop.</summary>
    public static void Attach() => Interlocked.Increment(ref _renderThreads);

    public static void Detach() => Interlocked.Decrement(ref _renderThreads);

    /// <summary>Wakes the render thread to look at its windows. Safe from any thread.</summary>
    public static void Wake()
    {
        if (Volatile.Read(ref _renderThreads) == 0)
        {
            LoopSignal.Request();
            return;
        }

        if (Interlocked.Exchange(ref _pending, 1) == 0)
        {
            Woken.Release();
        }
    }

    /// <summary>Blocks the render thread until woken or until <paramref name="timeout"/>; true when woken.</summary>
    public static bool Wait(TimeSpan timeout, CancellationToken token)
    {
        try
        {
            if (!Woken.Wait(timeout, token))
            {
                return false;
            }
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        Interlocked.Exchange(ref _pending, 0);
        return true;
    }
}
