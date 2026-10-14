using System;
using System.Collections.Generic;

namespace Adamantium.UI.Core;

/// <summary>Releases destroyed visuals in batches during idle time between frames, so subsystems keyed by them can let
/// go. Deferred so teardown stays cheap and elements that came back meanwhile are skipped.</summary>
public static class DiscardedVisuals
{
    /// <summary>A handler for <see cref="Discarded"/>. Its own delegate type because the batch is a
    /// <see cref="ReadOnlySpan{T}"/>, and a ref struct cannot be a generic argument - there is no
    /// <c>Action&lt;ReadOnlySpan&lt;T&gt;&gt;</c>.</summary>
    public delegate void DiscardedHandler(ReadOnlySpan<IFundamentalUIComponent> gone);

    /// <summary>Raised from the drain once per released batch. Handlers must not throw; the batch is a span so it cannot
    /// be kept.</summary>
    public static event DiscardedHandler Discarded;

    // Waiting to be released. Elements only - what holds them is asked at drain time, not now.
    private static readonly Queue<FundamentalUIComponent> Pending = new();

    // Reused across drains: the batch handed to subscribers.
    private static readonly List<IFundamentalUIComponent> Batch = new();

    private static long _releasedBatches;

    /// <summary>How many are still waiting. Zero means the last teardown has been fully paid for.</summary>
    public static int PendingCount { get { lock (Pending) return Pending.Count; } }

    /// <summary>Counts the batches released so far, so a reader holding visuals in a map of its own can tell, by one
    /// comparison, that some of them may have died since it last looked.</summary>
    public static long ReleasedBatches => System.Threading.Interlocked.Read(ref _releasedBatches);

    /// <summary>Called by <see cref="FundamentalUIComponent.MarkDiscarded"/> - the O(1) half of a teardown.</summary>
    internal static void Enqueue(FundamentalUIComponent gone)
    {
        if (gone == null) return;
        lock (Pending) Pending.Enqueue(gone);
    }

    /// <summary>Called by a teardown that has destroyed visuals: a control template being replaced, content being
    /// released. Records the state on each one and queues it; nothing is released here.</summary>
    public static void Publish(ReadOnlySpan<IFundamentalUIComponent> gone)
    {
        foreach (var component in gone)
        {
            (component as FundamentalUIComponent)?.MarkDiscarded();
        }
    }

    /// <summary>The single-element case. The span is over the parameter itself - a local - so this allocates nothing
    /// and needs no shared buffer to go wrong under re-entrancy.</summary>
    public static void Publish(IFundamentalUIComponent gone)
    {
        if (gone == null) return;

        Publish(System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(ref gone, 1));
    }

    /// <summary>Publishes <paramref name="root"/> and everything under it in the visual tree.</summary>
    public static void PublishSubtree(IUIComponent root)
    {
        if (root == null)
        {
            return;
        }

        var gone = new List<IFundamentalUIComponent>();
        Extensions.VisualTreeExtensions.TraverseVisualTree(root, component =>
        {
            if (component is IFundamentalUIComponent fundamental)
            {
                gone.Add(fundamental);
            }
        });
        Publish(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(gone));
    }

    /// <summary>Publishes each of <paramref name="roots"/> with everything under it and releases the whole queue at once:
    /// for a surface that draws no frames of its own to release between them - the designer, a headless build.</summary>
    public static void ReleaseNow(IEnumerable<IUIComponent> roots)
    {
        foreach (var root in roots)
        {
            PublishSubtree(root);
        }

        while (Drain(int.MaxValue) > 0)
        {
        }
    }

    /// <summary>Releases up to <paramref name="budget"/> queued elements, skipping any that came back, and returns how
    /// many were released.</summary>
    public static int Drain(int budget)
    {
        if (budget <= 0) return 0;

        Batch.Clear();
        lock (Pending)
        {
            while (Batch.Count < budget && Pending.Count > 0)
            {
                var candidate = Pending.Dequeue();
                if (candidate.Lifecycle == VisualLifecycle.Discarded) Batch.Add(candidate);
            }
        }

        if (Batch.Count == 0) return 0;

        // The element's own release first (its bindings and behaviors), then the subsystems keyed by it. In that
        // order because a subsystem's sweep may read the element, and an element that has let go of its bindings is
        // still a valid thing to read - whereas the reverse would have subsystems answering about an element whose
        // sources are still live.
        var batch = ReleasedBatches + 1;
        foreach (var component in Batch)
        {
            ((FundamentalUIComponent)component).ReleaseFromQueue(batch);
        }

        Discarded?.Invoke(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Batch));
        System.Threading.Interlocked.Increment(ref _releasedBatches);

        var released = Batch.Count;
        Batch.Clear();
        return released;
    }
}
