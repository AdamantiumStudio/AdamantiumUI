using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Diagnostics;

namespace Adamantium.UI.Core;

/// <summary>Runs style, measure and arrange for one visual root, draining only nodes in its dirty queues. A node finds its
/// manager through <see cref="For"/>.</summary>
public sealed class LayoutManager
{
    // Persistent per-root managers, keyed (weakly) by the top-most visual node so they are GC'd with their tree.
    private static readonly ConditionalWeakTable<IUIComponent, LayoutManager> Managers = new();

    // Backstop against a node that re-dirties itself every time it is laid out: bail the drain loop after this many iterations.
    private const int MaxPassIterations = 100;

    // No per-frame TIME budget: a pass always drains FULLY, so the drawn frame is internally consistent. An earlier budget
    // that cut a pass mid-way and re-queued the tail published TORN frames (a grid with tiles of two sizes). What replaced it:
    // the compositor presents at its own pace, and heavy INTAKE is bounded at the source (a virtualizing panel realizes only
    // viewport+margin, slicing big realizes over frames via InvalidateMeasureNextPass).

    private readonly IUIComponent _root;

    // Built on demand: For() creates a manager per parentless template element, and most of those are never used.
    private DirtyQueue _toStyle;
    private DirtyQueue _toMeasure;
    private DirtyQueue _toArrange;
    // Nodes asking to be re-measured NEXT pass, not this one (a virtualizing panel slicing a large realize over frames).
    // Enqueuing into ToMeasure would drain it THIS pass - the very burst we spread. Promoted at the start of each pass.
    private HashSet<IUIComponent> _toMeasureNextPass;
    private List<IUIComponent> _passBuffer;      // reused snapshot buffer for one phase's drain
    private List<IUIComponent> _promoteBuffer;   // reused scratch for promoting next-pass deferrals
    private System.Diagnostics.Stopwatch _passStopwatch;   // reused per pass (RuntimeStats)

    private DirtyQueue ToStyle => _toStyle ??= new DirtyQueue();
    private DirtyQueue ToMeasure => _toMeasure ??= new DirtyQueue();
    private DirtyQueue ToArrange => _toArrange ??= new DirtyQueue();
    private HashSet<IUIComponent> ToMeasureNextPass => _toMeasureNextPass ??= new HashSet<IUIComponent>();
    private List<IUIComponent> PassBuffer => _passBuffer ??= new List<IUIComponent>();
    private List<IUIComponent> PromoteBuffer => _promoteBuffer ??= new List<IUIComponent>();
    private System.Diagnostics.Stopwatch PassStopwatch => _passStopwatch ??= new System.Diagnostics.Stopwatch();

    public LayoutManager(IUIComponent root)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        lock (AllManagers) AllManagers.Add(new WeakReference<LayoutManager>(this));
    }

    /// <summary>Gets (creating once) the manager that owns the given root (a window at runtime, a subtree root in tests).</summary>
    public static LayoutManager GetOrCreate(IUIComponent root) => Managers.GetValue(root, static r => new LayoutManager(r));

    // TEMP (leak hunt): how many components ALL the dirty queues are holding right now, across every manager. A gauge
    // rather than a per-manager read, because there is one manager per ROOT and a theme swap recreates the controls
    // under it: a node enqueued and never drained is held for the life of the window, and the read has to cover the
    // managers nobody happens to be holding a reference to.
    public static long QueuedNow;

    // WEAK, and that is the whole point: a REGISTRY of managers would keep alive exactly the managers whose retention is
    // the question. A manager abandoned with a loaded queue is collected with it and holds nothing - which a counter of
    // enqueues-minus-drains cannot tell apart from a manager that is alive and holding.
    private static readonly List<WeakReference<LayoutManager>> AllManagers = new();

    /// <summary>TEMP (leak hunt): a CENSUS, not a counter - how many components the queues of the managers that are
    /// still ALIVE are holding right now, and how many managers there are.</summary>
    public static (int Nodes, int Managers, int Deferred) LayoutHeld
    {
        get
        {
            int nodes = 0, live = 0;
            lock (AllManagers)
            {
                for (var i = AllManagers.Count - 1; i >= 0; i--)
                {
                    if (!AllManagers[i].TryGetTarget(out var manager))
                    {
                        AllManagers.RemoveAt(i);
                        continue;
                    }

                    live++;
                    var counts = manager.QueuedCounts();
                    nodes += counts.Style + counts.Measure + counts.Arrange + counts.NextPass;
                }
            }

            return (nodes, live, DeferredMeasure.Count + DeferredArrange.Count);
        }
    }

    /// <summary>TEMP (leak hunt): the root each LIVE manager was made for. A manager is made per ROOT and, for an element
    /// with no root/owner/parent, per that ELEMENT - so after a theme swap this is a HANDLE on the retained orphans
    /// themselves, which is what nothing else in-process gives. Sampling them says what they are and where their parent
    /// chain leads; a dump can only say which single path gcroot happened to walk.</summary>
    public static List<IUIComponent> LiveManagerRoots()
    {
        var roots = new List<IUIComponent>();
        lock (AllManagers)
        {
            foreach (var handle in AllManagers)
            {
                if (handle.TryGetTarget(out var manager) && manager._root != null) roots.Add(manager._root);
            }
        }

        return roots;
    }

    /// <summary>TEMP experiment, NOT a fix: empty every live manager's queues so a collection can tell whether those
    /// queues are what HOLDS the departed controls, or merely list them while something else does.</summary>
    public static void DropAllQueuesForTheExperiment()
    {
        lock (AllManagers)
        {
            foreach (var handle in AllManagers)
            {
                if (!handle.TryGetTarget(out var manager)) continue;
                manager._toStyle = null;
                manager._toMeasure = null;
                manager._toArrange = null;
                manager._toMeasureNextPass = null;
                manager._passBuffer = null;
                manager._promoteBuffer = null;
            }
        }

        while (DeferredMeasure.TryDequeue(out _)) { }
        while (DeferredArrange.TryDequeue(out _)) { }
    }

    // TEMP (leak hunt): what THIS manager's queues are holding. They hold components strongly and the manager lives with
    // its root, so a node enqueued and never drained is retained for the life of the window.
    public (int Style, int Measure, int Arrange, int NextPass, int Deferred) QueuedCounts()
        => (_toStyle?.Count ?? 0, _toMeasure?.Count ?? 0, _toArrange?.Count ?? 0,
            _toMeasureNextPass?.Count ?? 0, DeferredMeasure.Count + DeferredArrange.Count);

    /// <summary>Resolves the manager responsible for <paramref name="node"/> via its top-most visual ancestor.</summary>
    public static LayoutManager For(IUIComponent node)
    {
        // Overlay content resolves to its layout owner's manager. Otherwise read the cached RootVisual, or walk to the
        // local top when detached.
        if (node.LayoutRoot is { } owner)
        {
            return GetOrCreate(owner);
        }

        var root = node.RootVisual;
        if (root != null)
        {
            return GetOrCreate(root);
        }

        var top = node;
        while (top.VisualParent != null)
        {
            top = top.VisualParent;
        }
        return GetOrCreate(top);
    }

    // Every invalidation owes the loop another pass, so wake it: layout is NOT covered by render-dirty marks, so without
    // this the loop only woke on its 250 ms safety timeout and a tab's content crawled in at ~4 passes/sec. See LoopSignal.
    public void InvalidateStyle(IUIComponent node)
    {
        ToStyle.Enqueue(node);
        LoopSignal.Request();
    }

    public void InvalidateMeasure(IUIComponent node)
    {
        LoopSignal.Request();
        // Parallel-rebind window: a rebind's synchronous writes flip AffectsMeasure -> InvalidateMeasure off worker threads,
        // which would concurrently mutate this root's (non-thread-safe) DirtyQueue. Collect lock-free, replay when the pass
        // ends (see BeginDeferredInvalidation).
        if (_deferInvalidations || OffPassThread) { DeferredMeasure.Enqueue(node); return; }
        // A measure-invalid node also needs re-arranging: enqueue both so arrange re-runs after measure recomputes sizes.
        ToMeasure.Enqueue(node);
        ToArrange.Enqueue(node);
    }

    public void InvalidateArrange(IUIComponent node)
    {
        LoopSignal.Request();
        if (_deferInvalidations || OffPassThread) { DeferredArrange.Enqueue(node); return; }
        ToArrange.Enqueue(node);
    }

    // True off the loop thread: deferred tab builds on a worker can reach these non-thread-safe queues.
    private static bool OffPassThread => _loopThreadId != 0 && Environment.CurrentManagedThreadId != _loopThreadId;

    private static volatile int _loopThreadId;

    // ---- Parallel-rebind deferred invalidation ----
    // While a virtualizing panel rebinds+measures its (disjoint) tiles across cores, the only shared escape is the per-root
    // DirtyQueue enqueue above; route those into these lock-free queues instead and replay on the coordinating thread once
    // the parallel pass joins. Static: the flag toggles around a Parallel.ForEach (fork/join barrier), so one switch suffices.
    private static volatile bool _deferInvalidations;
    private static readonly System.Collections.Concurrent.ConcurrentQueue<IUIComponent> DeferredMeasure = new();
    private static readonly System.Collections.Concurrent.ConcurrentQueue<IUIComponent> DeferredArrange = new();

    public static void BeginDeferredInvalidation() => _deferInvalidations = true;

    public static void EndDeferredInvalidation()
    {
        _deferInvalidations = false;
        while (DeferredMeasure.TryDequeue(out var n)) For(n).InvalidateMeasure(n);
        while (DeferredArrange.TryDequeue(out var n)) For(n).InvalidateArrange(n);
    }

    /// <summary>Requests <paramref name="node"/> be re-measured on the NEXT pass, not this one - a virtualizing panel
    /// continuing a sliced realize. Safe mid-pass: it doesn't touch this pass's queues.</summary>
    // Nothing else will wake the loop for this (the work is already known), so signal here or the fill stalls until the timeout.
    public void InvalidateMeasureNextPass(IUIComponent node)
    {
        ToMeasureNextPass.Add(node);
        LoopSignal.Request();
    }

    /// <summary>Raised at the end of a pass that actually did work (queues drained) - layout settled for this frame. Not
    /// raised on a clean frame, so a consumer (e.g. the render cache) can rebuild on this instead of every frame.</summary>
    public event EventHandler LayoutUpdated;

    /// <summary>Raised after a pass that found NO work: every queue empty AND nothing re-dirtied, so this tree has SETTLED.</summary>
    /// <remarks>
    /// Distinct from <see cref="LayoutUpdated"/> (which fires after a pass that DID work - mid-cascade, just one of several).
    /// A theme swap drains over several passes that each look "settled" in the LayoutUpdated sense; only a workless pass
    /// proves nothing is left. Static because a settle concerns whoever started the cascade, not one root (see <see cref="IsSettled"/>).
    /// </remarks>
    public static event Action<LayoutManager> Quiescent;

    /// <summary>True when this root owes no layout work at all (nothing queued, nothing deferred to the next pass).</summary>
    public bool IsSettled => ToStyle.IsEmpty && ToMeasure.IsEmpty && ToArrange.IsEmpty && ToMeasureNextPass.Count == 0;

    /// <summary>
    /// Runs one layout pass: drain style (themes can change templates, so it precedes measure), then measure, then arrange,
    /// ancestors-first within each. Re-dirtying during the pass loops until all queues drain.
    /// </summary>
    public void ExecuteLayoutPass()
    {
        // WHO owns these queues: whoever runs the pass. Stamped here rather than configured, because that is the one
        // place the answer is a fact. Everything off this thread routes aside - see InvalidateMeasure.
        _loopThreadId = Environment.CurrentManagedThreadId;

        // ...and take in whatever arrived from another thread since the last pass. Same replay as the parallel-rebind
        // window uses; the only difference is what put the nodes there.
        while (DeferredMeasure.TryDequeue(out var deferred)) For(deferred).InvalidateMeasure(deferred);
        while (DeferredArrange.TryDequeue(out var deferred)) For(deferred).InvalidateArrange(deferred);

        // Apply this frame's batched (coalesced) binding updates BEFORE laying out, so their target writes and the
        // invalidations they trigger drain in this same pass. The global queue flushes once/frame (first root empties it).
        BindingUpdateQueue.Flush();

        // Promote nodes that deferred to this pass. Snapshot+clear first: a promoted node may re-defer for the NEXT pass,
        // which must land in the now-empty set. InvalidateMeasure (not a bare enqueue) so the validity flag is cleared.
        if (ToMeasureNextPass.Count > 0)
        {
            PromoteBuffer.Clear();
            foreach (var node in ToMeasureNextPass) PromoteBuffer.Add(node);   // struct enumerator, no alloc
            ToMeasureNextPass.Clear();
            foreach (var node in PromoteBuffer)
                if (node is IMeasurableComponent measurable) measurable.InvalidateMeasure();
        }

        // Forward-progress safety net: if the root is dirty but was never enqueued (invalidated during construction, before
        // this manager existed), seed it now. O(1) - two flag reads - so a clean frame still costs nothing.
        if (_root is IMeasurableComponent rootMeasurable)
        {
            if (!rootMeasurable.IsMeasureValid) InvalidateMeasure(_root);
            else if (!rootMeasurable.IsArrangeValid) ToArrange.Enqueue(_root);
        }

        PassStopwatch.Restart();   // time the pass for RuntimeStats

        var didWork = false;
        var iterations = 0;
        while (!ToStyle.IsEmpty || !ToMeasure.IsEmpty || !ToArrange.IsEmpty)
        {
            if (++iterations > MaxPassIterations)
            {
                if (LayoutTrace.Enabled) LayoutTrace.Log($"LAYOUT PASS aborted after {MaxPassIterations} iterations (re-dirty loop)");
                break;
            }
            didWork = true;

            // Drain snapshots in style, measure, arrange order; work re-dirtied meanwhile waits for the next iteration.
            DrainPhase(ToStyle, ApplyTheme);
            DrainPhase(ToMeasure, MeasureDirty);
            DrainPhase(ToArrange, ArrangeDirty);
        }

        var settled = ToStyle.IsEmpty && ToMeasure.IsEmpty && ToArrange.IsEmpty;

        // A pass that found NOTHING to do is the "swap has settled" signal for RenderDirty.ForceStructuralUntilSettled
        // (theme/DPI): every settle write flows through this pass, so a workless pass means the cascade is done.
        if (!didWork)
        {
            RenderDirty.NotifyLayoutQuiescent();
            Quiescent?.Invoke(this);
        }

        RuntimeStats.LastLayoutPassMs = PassStopwatch.Elapsed.TotalMilliseconds;

        if (didWork) Diagnostics.LayoutTrace.Count(typeof(LayoutManager), "*pass*");
        RuntimeStats.LastPassBudgetDeferred = !settled;

        // LayoutUpdated = "layout settled this frame" - only when everything drained.
        if (didWork && settled)
            LayoutUpdated?.Invoke(this, EventArgs.Empty);

        // Coalesced resource-change notification: the style drain may have loaded a new theme's dictionaries, so fire
        // ResourcesChanged once, here, after they're present. No-op (a flag read) when nothing changed.
        UIAppContext.Current?.ResourceManager?.FlushResourceChanges();
    }

    // Drains one queue FULLY as a snapshot (work re-dirtied during the phase waits for the next iteration), ancestors-first.
    private void DrainPhase(DirtyQueue queue, Action<IUIComponent> process)
    {
        queue.DrainInto(PassBuffer);
        for (var i = 0; i < PassBuffer.Count; i++)
            process(PassBuffer[i]);
    }

    private static void ApplyTheme(IUIComponent node)
    {
        // Judging "is this worth theming" by tree membership was tried and REVERTED: it took the toolbar and the tab
        // strip out with the rest, because a node that merely MOVES from the old template into the new one is out of
        // the tree at the moment the queue is drained and is not dead at all. The teardown says so instead.
        if (node is FundamentalUIComponent { IsDiscarded: true })
        {
            return;
        }

        if (node is FundamentalUIComponent { IsStyleApplied: false } themed)
        {
            themed.ApplyCurrentTheme();
        }
    }

    private static void MeasureDirty(IUIComponent node)
    {
        var control = (IMeasurableComponent)node;
        if (control.IsMeasureValid) return;   // already measured this pass via an ancestor's cascade

        if (LayoutTrace.Enabled)
        {
            var name = string.IsNullOrEmpty(node.Name) ? node.GetType().Name : node.Name;
            LayoutTrace.Log($"MEASURE-DIRTY {name}");
        }

        var before = control.DesiredSize;

        // Re-measure with the element's OWN cached constraint, NOT a guess; MeasureOverride cascades down, the validity gate
        // skipping any clean subtree. A root visual uses its client size; a never-measured top uses its Width/Height.
        if (node is IRootVisualComponent root)
        {
            MeasureControl(control, root.ClientWidth, root.ClientHeight);
        }
        else if (control.PreviousMeasureConstraint is { } cached)
        {
            control.Measure(cached);
        }
        else
        {
            MeasureControl(control, control.Width, control.Height);
        }

        // Propagate up only if the child's OUTWARD size changed (else the re-measure stayed contained). EXCEPT a parent that
        // is a MEASURE BOUNDARY (fixed size, or a virtualizing host whose extent is count×cell): propagating in is spurious
        // and, running outside the panel's _inLayout mute, re-dirties it every iteration and spins to MaxPassIterations
        // (draining the whole realize backlog in one pass). Honor the boundary so InvalidateMeasureNextPass is respected.
        if (control.DesiredSize != before
            && node.VisualParent is IMeasurableComponent { IsMeasureValid: true } parent
            && !parent.IsMeasureBoundary)
        {
            parent.InvalidateMeasure();
        }
    }

    private void ArrangeDirty(IUIComponent node)
    {
        var control = (IMeasurableComponent)node;
        if (control.IsArrangeValid) return;

        if (node.Visibility == Visibility.Collapsed) return;

        if (!control.IsMeasureValid)
        {
            if (ToMeasure.Contains(node))
            {
                ToArrange.Enqueue(node);
            }

            return;
        }

        // Arrange into the node's own last slot; a never-arranged node fills its measured area, and the root uses the live
        // client rect.
        var slot = node is IRootVisualComponent { ClientWidth: > 0, ClientHeight: > 0 } root
            ? new Rect(0, 0, root.ClientWidth, root.ClientHeight)
            : control.PreviousArrangeSlot ?? new Rect(control.DesiredSize);
        if (LayoutTrace.Enabled)
        {
            var name = string.IsNullOrEmpty(node.Name) ? node.GetType().Name : node.Name;
            LayoutTrace.Log($"ARRANGE-DIRTY {name}: -> Arrange({slot})");
        }
        control.Arrange(slot);
    }

    private static void MeasureControl(IMeasurableComponent control, Double width, Double height)
    {
        if (!Double.IsNaN(width) && !Double.IsNaN(height))
        {
            control.Measure(new Size(width, height));
        }
        else if (Double.IsNaN(width) && !Double.IsNaN(height))
        {
            control.Measure(new Size(Double.PositiveInfinity, height));
        }
        else if (!Double.IsNaN(width) && Double.IsNaN(height))
        {
            control.Measure(new Size(width, Double.PositiveInfinity));
        }
        else
        {
            control.Measure(Size.Infinity);
        }
    }

    /// <summary>
    /// A set of dirty nodes drained ancestors-first: dedup via a membership set, ordered by visual depth via a min-heap so a
    /// parent is processed before its children (their cascade validates them, so their own dequeue no-ops on the gate).
    /// Depth is computed at enqueue time; a stale order only costs redundant work, not correctness (the validity gates make
    /// re-processing safe).
    /// </summary>
    private sealed class DirtyQueue
    {
        private readonly PriorityQueue<IUIComponent, int> _heap = new();
        private readonly HashSet<IUIComponent> _members = new();

        public bool IsEmpty => _members.Count == 0;

        public bool Contains(IUIComponent node) => _members.Contains(node);

        internal int Count => _members.Count;

        public void Enqueue(IUIComponent node)
        {
            if (!_members.Add(node)) return;
            System.Threading.Interlocked.Increment(ref QueuedNow);
            _heap.Enqueue(node, Depth(node));
        }

        /// <summary>Removes all currently-queued nodes into <paramref name="buffer"/> in ancestors-first (depth) order.
        /// Nodes enqueued AFTER this returns (re-dirtied during processing) stay queued for the next drain.</summary>
        public void DrainInto(List<IUIComponent> buffer)
        {
            buffer.Clear();
            while (_heap.Count > 0)
            {
                var node = _heap.Dequeue();
                if (_members.Remove(node))
                {
                    System.Threading.Interlocked.Decrement(ref QueuedNow);
                    buffer.Add(node);
                }
            }
        }

        private static int Depth(IUIComponent node)
        {
            var depth = 0;
            var parent = node.VisualParent;
            while (parent != null)
            {
                depth++;
                parent = parent.VisualParent;
            }
            return depth;
        }
    }
}
