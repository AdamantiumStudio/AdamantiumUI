using System.Collections.Generic;
using Adamantium.UI.Core.Diagnostics;

namespace Adamantium.UI.Core.Data;

/// <summary>Coalesces source-to-target binding pushes to one apply per binding per frame. <see cref="Flush"/> runs before
/// layout and drains dependent chains; Enqueue is thread-safe.</summary>
public static class BindingUpdateQueue
{
    private static readonly object Sync = new();
    private static readonly HashSet<BindingExpressionBase> Dirty = new();
    private static readonly List<BindingExpressionBase> Batch = new();   // reused snapshot buffer

    /// <summary>F2 budget: the maximum number of binding updates applied per <see cref="Flush"/> (i.e. per frame).
    /// Default 10000 - high enough that a normal frame's handful of updates never hit it, low enough to bound a binding
    /// storm (anything over the cap stays dirty and drains over later frames). Set to 0 to disable (apply all each
    /// frame), or raise/lower it (e.g. 50000) to taste.</summary>
    public static int MaxAppliesPerFlush { get; set; } = 10000;

    /// <summary>Marks an expression for the next coalesced flush (deduped: enqueuing twice still applies once).</summary>
    public static void Enqueue(BindingExpressionBase expression)
    {
        lock (Sync) Dirty.Add(expression);
        LoopSignal.Request();   // a value is waiting to be pushed to its target - the loop owes a frame (see LoopSignal)
    }

    /// <summary>Drops a (e.g. closed) expression so a dead binding is never applied.</summary>
    public static void Remove(BindingExpressionBase expression)
    {
        lock (Sync) Dirty.Remove(expression);
    }

    /// <summary>How many times a single <see cref="Flush"/> re-drains what its own applies dirtied. Deep enough for any
    /// real dependent chain, finite so a pair of bindings that never settle costs a few rounds a frame instead of hanging
    /// the loop - the leftovers drain over later frames, exactly as an over-budget batch does.</summary>
    private const int MaxCascadeRounds = 8;

    /// <summary>Applies every pending binding update (coalesced), including the ones those applies dirty in turn, so the
    /// graph is settled before layout reads it.</summary>
    public static void Flush()
    {
        // The failures reported BEFORE this flush have had their frame to mend; what is still broken after it is broken.
        var suspects = BindingTrace.TakeSuspects();

        // Drain the cascade now so layout never sees one of two dependent bindings stale; the budget spans all rounds.
        var remaining = MaxAppliesPerFlush > 0 ? MaxAppliesPerFlush : int.MaxValue;
        for (var round = 0; round < MaxCascadeRounds && remaining > 0; round++)
        {
            var applied = FlushOnce(remaining);
            if (applied == 0)
            {
                break;
            }

            remaining -= applied;
        }

        BindingTrace.Confirm(suspects);
    }

    /// <summary>One coalesced pass over the currently-dirty expressions; returns how many were applied.</summary>
    private static int FlushOnce(int budget)
    {
        lock (Sync)
        {
            if (Dirty.Count == 0) return 0;
            Batch.Clear();
            if (Dirty.Count > budget)
            {
                // Over budget: apply only the first N this flush; the rest stay dirty and drain over later frames.
                foreach (var expression in Dirty)
                {
                    Batch.Add(expression);
                    if (Batch.Count >= budget) break;
                }
                foreach (var expression in Batch)
                    Dirty.Remove(expression);
            }
            else
            {
                Batch.AddRange(Dirty);
                Dirty.Clear();
            }
        }
        // Apply OUTSIDE the lock: ApplyPending runs converters + SetValue and can re-enter Enqueue (dependent bindings),
        // which would deadlock under the lock; those re-enqueues land in Dirty for the next round.
        var count = Batch.Count;
        foreach (var expression in Batch)
            expression.ApplyPending();   // diagnostics counted in UpdateTarget (the actual target write), not here
        Batch.Clear();
        return count;
    }
}
