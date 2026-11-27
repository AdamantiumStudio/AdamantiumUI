namespace Adamantium.UI.Audio.Tests;

/// <summary>A thread's context as a UI loop is: what is posted to it waits for the test to run it, on its own thread.
/// </summary>
internal sealed class QueuedContext : SynchronizationContext
{
    private readonly Queue<(SendOrPostCallback Callback, object State)> _posted = new();

    public override void Post(SendOrPostCallback d, object state)
    {
        lock (_posted)
        {
            _posted.Enqueue((d, state));
        }
    }

    /// <summary>Runs what was posted, and what that posts, until nothing is left.</summary>
    public void Run()
    {
        while (true)
        {
            (SendOrPostCallback Callback, object State) next;
            lock (_posted)
            {
                if (!_posted.TryDequeue(out next))
                {
                    return;
                }
            }

            next.Callback(next.State);
        }
    }
}
