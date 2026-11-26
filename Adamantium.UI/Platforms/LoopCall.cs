using System;
using System.Threading;
using Adamantium.UI.Core;

namespace Adamantium.UI.Platforms;

/// <summary>Work an assistive technology asks of the elements, which live on the application's loop: run there, and
/// waited for as long as it may be. Work the loop has not started when the wait ends is not run later.</summary>
internal static class LoopCall
{
    private const int Pending = 0;
    private const int Started = 1;
    private const int Abandoned = 2;

    /// <summary>Runs <paramref name="work"/> where <paramref name="post"/> runs work - at once on the loop's own thread -
    /// and gives what it gave, or throws what it threw; <see cref="TimeoutException"/> when it does not finish within
    /// <paramref name="timeout"/>, twice over once it has started.</summary>
    public static T Run<T>(Action<Action> post, Func<T> work, TimeSpan timeout)
    {
        if (LoopSignal.IsLoopThread)
        {
            return work();
        }

        T result = default;
        Exception failure = null;
        var state = Pending;
        var done = new ManualResetEventSlim();
        post(() =>
        {
            if (Interlocked.CompareExchange(ref state, Started, Pending) != Pending)
            {
                return;
            }

            try
            {
                result = work();
            }
            catch (Exception e)
            {
                failure = e;
            }
            finally
            {
                done.Set();
            }
        });

        if (!done.Wait(timeout))
        {
            if (Interlocked.CompareExchange(ref state, Abandoned, Pending) == Pending)
            {
                done.Dispose();
                throw new TimeoutException("The application did not answer in time.");
            }

            if (!done.Wait(timeout))
            {
                throw new TimeoutException("The application is still answering.");
            }
        }

        done.Dispose();
        if (failure != null)
        {
            // Thrown as it was on the loop, where it was thrown.
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return result;
    }
}
