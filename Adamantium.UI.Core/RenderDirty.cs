using System.Collections.Generic;

namespace Adamantium.UI.Core;

/// <summary>Per-frame record of what changed, so the render cache redoes only that: Geometry (re-render those), Transform
/// (re-bake world transforms) and Structural (full walk). A clean frame replays retained units.</summary>
/// <remarks>Conservative: over-marking only costs time, and every mutation path marks at least one kind.</remarks>
public sealed class RenderDirtyScope
{
    private readonly HashSet<IUIComponent> GeometrySet = new();
    private bool _transform;
    private bool _structural;

    // Forces full walks while a theme or DPI swap settles, since some of its writes are unmarked; cleared after the build
    // of the first frame whose layout pass found no work.
    private bool _forceUntilSettled;
    private bool _finalForcedBuild;

    // Monotonic test hook (like MeasurableUIComponent.TotalMeasureCalls): recycling-ring tests assert ZERO structural
    // marks per continuous-scroll step, which is how they catch attach/detach/Visibility churn headlessly.
    public long TotalStructuralMarks;

    /// <summary>Records that <paramref name="component"/>'s recorded geometry changed - it will re-render.</summary>
    public void MarkGeometry(IUIComponent component)
    {
        // Locked: writers include parallel arrange and the message-pump thread, so every read and clear takes this lock.
        // Diagnostic counters stay lock-free.
        if (component == null) return;
        lock (GeometrySet) GeometrySet.Add(component);
        LoopSignal.Request();   // the scene changed - the loop owes a frame
    }

    /// <summary>Marks <paramref name="root"/> and everything drawn under it as re-rendering. A hidden element hides its
    /// subtree with it, so all of them must say what they draw now (which, while it is hidden, is nothing).</summary>
    public void MarkSubtreeGeometry(IUIComponent root)
    {
        if (root == null) return;

        // Listing it as dirty is not enough: a component whose OWN geometry still reads valid is stepped over by the
        // partial record (it would re-render to the same commands), so a child of something just shown would never say
        // it draws again and its group would stay empty - the close button's glyph missing on hover.
        root.InvalidateRender(false);
        MarkGeometry(root);
        foreach (var child in root.VisualChildren) MarkSubtreeGeometry(child);
    }

    /// <summary>Atomically snapshot the geometry-dirty set into <paramref name="buffer"/> under the same lock
    /// <see cref="MarkGeometry"/> writes with, so a concurrent mark (parallel arrange / a Dispatcher-thread invalidation)
    /// can't corrupt the enumeration. The set itself is NOT cleared here (the build clears it via <see cref="Clear"/>).</summary>
    public void SnapshotGeometryInto(List<IUIComponent> buffer)
    {
        buffer.Clear();
        lock (GeometrySet) buffer.AddRange(GeometrySet);
    }

    /// <summary>The geometry-dirty count, read under the write lock (safe against a concurrent mark).</summary>
    public int GeometryCount { get { lock (GeometrySet) return GeometrySet.Count; } }

    // Paint-dirty: same commands, new colors; the applier re-bakes existing units without re-rendering. Keeps an animated
    // shared brush cheap.
    private readonly HashSet<IUIComponent> PaintSet = new();

    /// <summary>Records that only <paramref name="component"/>'s PAINT changed - same shape, same commands, new colour.</summary>
    public void MarkPaint(IUIComponent component)
    {
        if (component == null) return;
        lock (PaintSet)
        {
            PaintSet.Add(component);
            _paintMarks++;
        }
        LoopSignal.Request();
    }

    private long _paintMarks;

    /// <summary>How many paint marks this scope has EVER taken. Monotonic and untouched by <see cref="Clear"/>, so a
    /// stage that runs on the render thread can still tell whether a recolour happened since it last redrew: the set
    /// itself is wiped once per frame by the loop thread, long before an overlay stage gets to look at it.</summary>
    public long TotalPaintMarks { get { lock (PaintSet) return _paintMarks; } }

    /// <summary>Atomically snapshot the paint-dirty set (same locking discipline as <see cref="SnapshotGeometryInto"/>).</summary>
    public void SnapshotPaintInto(List<IUIComponent> buffer)
    {
        buffer.Clear();
        lock (PaintSet) buffer.AddRange(PaintSet);
    }

    public int PaintCount { get { lock (PaintSet) return PaintSet.Count; } }

    // Assigning a different brush still re-records the element: a one-element case, not worth coupling to the renderer.

    // Which components moved this frame, so the recorder refreshes only their layout-snapshot entries instead of
    // re-capturing it all.
    private readonly HashSet<IUIComponent> MovedSet = new();

    // A move whose COMPONENT we can't name (a Transform ticking while it is not assigned as anyone's RenderTransform, so
    // Transform.Owner is null). Then the incremental refresh above cannot know what went stale, and the recorder falls back
    // to re-capturing the whole snapshot for that frame. Correctness over cleverness: an unnameable mover is rare (an
    // orphaned/re-assigned transform), and the fallback is exactly the pre-incremental behaviour.
    private bool _transformUnknown;

    /// <summary>Records that <paramref name="component"/> MOVED (world transforms must be re-baked; no re-record). Pass the
    /// component that moved - null only when the mover genuinely has no owner (see <see cref="IsTransformUnknown"/>).</summary>
    public void MarkTransform(IUIComponent component)
    {
        _transform = true;
        LoopSignal.Request();
        if (component == null) { _transformUnknown = true; return; }
        lock (MovedSet) MovedSet.Add(component);   // locked: movers arrive from the parallel arrange too (see MarkGeometry)
    }

    /// <summary>Atomically snapshot the moved components into <paramref name="buffer"/> (same lock/race as
    /// <see cref="SnapshotGeometryInto"/>).</summary>
    public void SnapshotMovedInto(List<IUIComponent> buffer)
    {
        buffer.Clear();
        lock (MovedSet) buffer.AddRange(MovedSet);
    }

    /// <summary>True when something moved that <see cref="MarkTransform"/> could not name - the frozen layout snapshot
    /// can't be refreshed incrementally this frame and must be re-captured wholesale.</summary>
    public bool IsTransformUnknown => _transformUnknown;

    // MOTION NODES that moved this frame (their subtrees translate as a unit - a scrolled panel). Unlike the global
    // _transform flag, a node move doesn't invalidate anyone's baked geometry: instances under the node reference its
    // transform-table slot, so the render just rewrites the node's matrix (64 bytes) and replays - THE O(1)-scroll path.
    private readonly HashSet<IUIComponent> NodeSet = new();

    /// <summary>Records that a MOTION NODE moved (its table slot must be rewritten; nothing re-bakes/re-records).
    /// Locked for the same parallel-arrange reason as <see cref="MarkGeometry"/>.</summary>
    public void MarkNodeTransform(IUIComponent node)
    {
        LoopSignal.Request();
        if (node == null) return;
        lock (NodeSet) NodeSet.Add(node);
    }

    /// <summary>The moved motion nodes (valid until <see cref="Clear"/>).</summary>
    public IReadOnlyCollection<IUIComponent> MovedNodes => NodeSet;

    /// <summary>Atomically snapshot the moved-node set into <paramref name="buffer"/> under the same lock
    /// <see cref="MarkNodeTransform"/> writes with (same race as <see cref="SnapshotGeometryInto"/>).</summary>
    public void SnapshotNodesInto(List<IUIComponent> buffer)
    {
        buffer.Clear();
        lock (NodeSet) buffer.AddRange(NodeSet);
    }

    // WHICH components entered or left the drawn set this frame (a visual child added/removed, a Visibility toggle). Recorded
    // like MarkTransform's movers, and for the same reason: knowing the identities is what will let the recorder splice just
    // those components instead of re-recording the whole tree. Today NOTHING reads this - the recorder still does the full walk
    // it always did - so this step is pure bookkeeping and cannot change behaviour. It only makes the next one possible.
    private readonly HashSet<IUIComponent> StructuralSet = new();

    // A structural change nobody could name (a caller with no component to hand). The incremental path can then know nothing
    // about what changed, so it must not be taken at all.
    private bool _structuralUnknown;

    /// <summary>Records a structural change (add/remove/visibility) of <paramref name="component"/> - it entered or left the
    /// drawn set, so the paint-order list must be rebuilt. Pass null ONLY when the change genuinely cannot be attributed to a
    /// component (see <see cref="IsStructuralUnknown"/>).</summary>
    // Something LEFT the visual tree. Its cached groups are withdrawn by the renderer's reconcile, which used to run only
    // inside a full walk - and full walks are rare by design now, so a view that left kept its place in the paint order and
    // the retained op stream went on re-issuing it: the tab that was left drawn on top of the tab that replaced it.
    private long _detachGeneration;

    public void MarkDetached()
    {
        System.Threading.Interlocked.Increment(ref _detachGeneration);
        // ...and ask for a frame: withdrawing it happens in a BUILD, and on an otherwise idle scene there is no next
        // build to ride on - the departed view would simply stay on screen until something else asked for one.
        LoopSignal.Request();
    }

    public long DetachGeneration => System.Threading.Interlocked.Read(ref _detachGeneration);

    public void MarkStructural(IUIComponent component = null)
    {
        _structural = true;
        LoopSignal.Request();
        TotalStructuralMarks++;
        if (component == null) { _structuralUnknown = true; return; }
        lock (StructuralSet) StructuralSet.Add(component);   // locked: marks arrive from the parallel arrange too
    }

    /// <summary>Atomically snapshot the structurally-changed components (same lock/race as <see cref="SnapshotGeometryInto"/>).</summary>
    public void SnapshotStructuralInto(List<IUIComponent> buffer)
    {
        buffer.Clear();
        lock (StructuralSet) buffer.AddRange(StructuralSet);
    }

    /// <summary>How many components are structurally marked, read under the write lock.</summary>
    public int StructuralCount { get { lock (StructuralSet) return StructuralSet.Count; } }

    /// <summary>Whether the recorder must re-walk the whole tree: an unnamed structural change, or a swap settling (see
    /// <see cref="ForceStructuralUntilSettled"/>).</summary>
    public bool IsStructuralUnknown => _structuralUnknown || _forceUntilSettled || _finalForcedBuild;

    /// <summary>Force full structural rebuilds until the layout signals it has fully settled (see
    /// <see cref="_forceUntilSettled"/>). Call when starting a multi-frame state swap (theme, DPI).</summary>
    public void ForceStructuralUntilSettled() { _forceUntilSettled = true; LoopSignal.Request(); }

    /// <summary>Called by the layout manager after a pass that found NO work (every queue empty) - the settle signal for
    /// <see cref="ForceStructuralUntilSettled"/>. The current frame's build stays forced (the final walk, which follows
    /// this pass and so sees every settle write); after it, forcing ends.</summary>
    public void NotifyLayoutQuiescent()
    {
        if (!_forceUntilSettled) return;
        _forceUntilSettled = false;
        _finalForcedBuild = true;
    }

    /// <summary>TEMP (leak hunt): how many of the marks name a component the template teardown has DESTROYED. These sets
    /// hold their components STRONGLY and a scope lives as long as the stage that owns it, so a mark left behind by a
    /// discarded part is a permanent hold. Counted, not sized: the render cache's snapshot map held 39 dead controls a
    /// swap while its SIZE never moved, and that is what hid it.</summary>
    public (int Geometry, int Paint, int Moved, int Node, int Structural) DeadMarks()
    {
        return (Dead(GeometrySet), Dead(PaintSet), Dead(MovedSet), Dead(NodeSet), Dead(StructuralSet));

        static int Dead(HashSet<IUIComponent> set)
        {
            var n = 0;
            foreach (var component in set)
                if (component is FundamentalUIComponent { IsDiscarded: true }) n++;
            return n;
        }
    }

    /// <summary>Any dirty state at all (else the frame is fully clean).</summary>
    public bool HasWork => _structural || _transform || GeometrySet.Count > 0 || PaintSet.Count > 0
                                  || NodeSet.Count > 0 || _forceUntilSettled || _finalForcedBuild;

    public bool IsStructural => _structural || _forceUntilSettled || _finalForcedBuild;
    public bool IsTransform => _transform;

    /// <summary>True while a multi-frame structural state swap (resize / DPI / theme) is still settling. The
    /// decoupled render path uses this as a lightweight barrier: it records such a window INLINE (record + apply together
    /// in BeginDraw) instead of at loop level, so the packet can't straddle the swap's per-frame relayout + presenter /
    /// projection finalisation and desync (chrome left at the old size while dirty content re-records at the new one).</summary>
    public bool IsSettlingStructural => _forceUntilSettled || _finalForcedBuild;

    /// <summary>The geometry-dirty components to re-render this build (only valid until <see cref="Clear"/>).</summary>
    public IReadOnlyCollection<IUIComponent> Geometry => GeometrySet;

    /// <summary>Reset after a build has consumed the dirty state.</summary>
    public void Clear()
    {
        // Clear the HashSets under the SAME locks their writers (MarkGeometry/MarkNodeTransform/MarkTransform) take - a
        // lock-free Clear racing a concurrent Add (parallel arrange / Dispatcher-thread invalidation) corrupted the set.
        lock (GeometrySet) GeometrySet.Clear();
        lock (PaintSet) PaintSet.Clear();
        lock (NodeSet) NodeSet.Clear();
        lock (MovedSet) MovedSet.Clear();
        lock (StructuralSet) StructuralSet.Clear();
        _transform = false;
        _transformUnknown = false;
        _structural = false;
        _structuralUnknown = false;
        _finalForcedBuild = false;   // the post-settle walk ran; forcing ends (_forceUntilSettled survives Clear by design)
    }

}


