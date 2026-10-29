using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Adamantium.UI;

/// <summary>One of a few places the user's processes take by name - a copy of an application among those allowed to
/// run (<see cref="UIApplication.MaxInstances"/>), the editor holding a solution: a named mutex of the user's, in every
/// session of theirs, held by a thread of its own until it is given up. A process that ends - closed or crashed - leaves
/// its mutex abandoned, and the next one takes it, so no place is lost.</summary>
public sealed class InstanceSlot : IDisposable
{
    private readonly ManualResetEventSlim _released = new();
    private Thread _holder;
    private bool _disposed;

    private InstanceSlot()
    {
    }

    /// <summary>Takes a free place of the <paramref name="count"/> named <paramref name="name"/>; null when every one
    /// is taken - by another process, or by this one.</summary>
    public static InstanceSlot TryTake(string name, int count = 1)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        var slot = new InstanceSlot();
        using var settled = new ManualResetEventSlim();
        var taken = false;
        var holder = new Thread(() =>
        {
            using var mutex = TakeFree(name, count);
            taken = mutex != null;
            settled.Set();
            if (mutex == null)
            {
                return;
            }

            slot._released.Wait();
            mutex.ReleaseMutex();
        })
        {
            IsBackground = true,
            Name = "AdamantiumInstanceSlot"
        };
        holder.Start();
        settled.Wait();
        slot._holder = holder;
        return taken ? slot : null;
    }

    /// <summary>Whether the one place named <paramref name="name"/> is taken - by another process, or by this one.
    /// </summary>
    public static bool IsTaken(string name)
    {
        using var slot = TryTake(name);
        return slot == null;
    }

    /// <summary>Gives the place up, done by the time it returns: the next process finds it free.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _released.Set();
        _holder.Join();
    }

    /// <summary>The name of the mutex of place <paramref name="index"/> of those named <paramref name="name"/>.</summary>
    internal static string CreateName(string name, int index) =>
        $"adamantium-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name)), 0, 12).ToLowerInvariant()}-{index}";

    /// <summary>The mutexes are the user's, in every session of theirs.</summary>
    internal static NamedWaitHandleOptions Options => new() { CurrentUserOnly = true, CurrentSessionOnly = false };

    private static Mutex TakeFree(string name, int count)
    {
        for (var index = 0; index < count; index++)
        {
            var mutex = new Mutex(false, CreateName(name, index), Options, out _);
            try
            {
                if (mutex.WaitOne(0))
                {
                    return mutex;
                }
            }
            catch (AbandonedMutexException)
            {
                return mutex;
            }

            mutex.Dispose();
        }

        return null;
    }
}
