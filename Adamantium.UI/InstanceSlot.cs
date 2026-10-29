using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Adamantium.UI;

/// <summary>One of the places a limited number of copies of an application run in: a named mutex of the user's, held by
/// a thread of its own for as long as the copy runs. A copy that ends - closed or crashed - leaves its mutex abandoned,
/// and the next copy takes it, so no place is lost.</summary>
internal sealed class InstanceSlot : IDisposable
{
    private readonly ManualResetEventSlim _released = new();
    private bool _disposed;

    private InstanceSlot()
    {
    }

    /// <summary>Takes a free place of <paramref name="count"/> for the application whose data is in
    /// <paramref name="dataFolder"/>; null when every one is taken.</summary>
    public static InstanceSlot TryTake(string dataFolder, int count)
    {
        var slot = new InstanceSlot();
        using var settled = new ManualResetEventSlim();
        var taken = false;
        var holder = new Thread(() =>
        {
            using var mutex = TakeFree(dataFolder, count);
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
        return taken ? slot : null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _released.Set();
    }

    /// <summary>The name of the mutex of the place <paramref name="index"/> of the application whose data is in
    /// <paramref name="dataFolder"/>.</summary>
    internal static string CreateName(string dataFolder, int index) => $"adamantium-{CreateKey(dataFolder)}-{index}";

    /// <summary>The mutexes are the user's, in every session of theirs.</summary>
    internal static NamedWaitHandleOptions Options => new() { CurrentUserOnly = true, CurrentSessionOnly = false };

    private static Mutex TakeFree(string dataFolder, int count)
    {
        for (var index = 0; index < count; index++)
        {
            var mutex = new Mutex(false, CreateName(dataFolder, index), Options, out _);
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

    private static string CreateKey(string dataFolder)
    {
        var folder = Path.GetFullPath(dataFolder);
        if (!OperatingSystem.IsLinux())
        {
            folder = folder.ToUpperInvariant();
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(folder)), 0, 12).ToLowerInvariant();
    }
}
