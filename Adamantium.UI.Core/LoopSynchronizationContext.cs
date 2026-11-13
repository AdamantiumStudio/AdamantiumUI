using System;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Adamantium.UI.Core;

/// <summary>
/// The loop thread's synchronization context: what follows an <c>await</c> there goes on there, posted through
/// <see cref="LoopSignal"/>, and not on the thread pool - the visual tree is the loop thread's alone. The dispatcher's
/// context belongs to the thread pumping the system's messages, another thread. Never wait on a task synchronously on
/// the loop thread: what that task awaits comes back to the loop, which is busy waiting.
/// </summary>
public sealed class LoopSynchronizationContext : SynchronizationContext
{
    public override void Post(SendOrPostCallback callback, object state)
    {
        ArgumentNullException.ThrowIfNull(callback);
        LoopSignal.Post(() => callback(state));
    }

    /// <summary>Runs <paramref name="callback"/> on the loop thread and waits for it: at once on the loop thread itself.
    /// </summary>
    public override void Send(SendOrPostCallback callback, object state)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (LoopSignal.IsLoopThread)
        {
            callback(state);
            return;
        }

        using var done = new ManualResetEventSlim(false);
        ExceptionDispatchInfo failure = null;
        LoopSignal.Post(() =>
        {
            try
            {
                callback(state);
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                done.Set();
            }
        });
        done.Wait();
        failure?.Throw();
    }

    public override SynchronizationContext CreateCopy() => this;
}
