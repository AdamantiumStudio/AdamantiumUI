using System;
using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Threading;
using Adamantium.UI.Core;

namespace Adamantium.UI.Controls;

// Changes a source raises on another thread - a game world's collections, changed on its own loop - carried over to the
// UI thread and applied there in the order they came.
internal sealed class CrossThreadChanges<T>
{
    private ConcurrentQueue<T> _pending;   // made by the first change from another thread: most receivers never see one
    private readonly Action<T> _apply;
    private readonly Action _drain;
    private int _uiThread;
    private int _posted;

    public CrossThreadChanges(Action<T> apply)
    {
        _apply = apply;
        _drain = Drain;
    }

    // The thread attaching the receiver to a source. With the loop's own thread it is the receiver's: a view is built off
    // the loop and then lives on it, and a change from either is applied at once.
    public void TakeThisThread() => _uiThread = Environment.CurrentManagedThreadId;

    // Queues a change raised on another thread and returns true; on the receiver's own applies what is waiting - it came
    // first - and returns false, for the caller to apply this one.
    public bool Defer(T change)
    {
        if (Environment.CurrentManagedThreadId == _uiThread || LoopSignal.IsLoopThread)
        {
            if (_pending is { IsEmpty: false }) Drain();
            return false;
        }

        if (_pending == null) Interlocked.CompareExchange(ref _pending, new ConcurrentQueue<T>(), null);
        _pending.Enqueue(change);
        Post();
        return true;
    }

    // Drops what is waiting: the source was let go of, or read again under its lock and so holds those changes already.
    public void Clear()
    {
        if (_pending is { IsEmpty: false } pending) pending.Clear();
    }

    private void Post()
    {
        if (Interlocked.Exchange(ref _posted, 1) == 0) LoopSignal.Post(_drain);
    }

    private void Drain()
    {
        Volatile.Write(ref _posted, 0);
        if (_pending is not { } pending) return;

        try
        {
            while (pending.TryDequeue(out var change)) _apply(change);
        }
        finally
        {
            // One that threw leaves the rest for the next frame rather than for the next change from the other thread.
            if (!pending.IsEmpty) Post();
        }
    }
}

// Holds the lock a source makes and announces its changes under, when it has one: read under it, the source is never
// seen halfway through a change, and no change raised on another thread waits that the reading already holds.
internal readonly struct SourceGate : IDisposable
{
    private readonly object _gate;

    private SourceGate(object gate)
    {
        _gate = gate;
        if (gate != null) Monitor.Enter(gate);
    }

    public static SourceGate Hold(IEnumerable source) =>
        new(source is INotifyCollectionChanged and ICollection collection ? collection.SyncRoot : null);

    // The items of the source held, copied into an array rented from the shared pool (give it back with Return) - so
    // what is done with them happens after the lock is let go, and the source's own thread never waits on it. Null
    // when the source has no lock: it is read as it is.
    public object[] Copy(IEnumerable source, out int count)
    {
        count = 0;
        if (_gate == null) return null;

        var collection = (ICollection)source;
        count = collection.Count;
        var copy = ArrayPool<object>.Shared.Rent(Math.Max(count, 1));
        collection.CopyTo(copy, 0);
        return copy;
    }

    public static void Return(object[] copy)
    {
        if (copy != null) ArrayPool<object>.Shared.Return(copy, clearArray: true);
    }

    public void Dispose()
    {
        if (_gate != null) Monitor.Exit(_gate);
    }
}
