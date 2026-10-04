using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Adamantium.Graphics.Core;
using Adamantium.Mathematics;
using Adamantium.UI.Core;

namespace Adamantium.UI.Rendering;

public partial class RenderCache
{
    private Matrix4x4F _projectionMatrix;

    // Frame-scoped world-transform memo. WorldTransform is O(depth) live and read many times per unit (ResolveScissor
    // walks every clip ancestor) -> O(depth^2) naive. Transforms are stable within a frame, so compose each ONCE here:
    // World(c) = LocalTransform * World(parent) => O(nodes)/frame. Render-path only; WorldTransform stays live for hit-test.
    private readonly Dictionary<IUIComponent, Matrix4x4F> _worldCache = new();

    // RECORDER-owned frozen layout (the authority, mutated on the update thread). The APPLIER never reads it - it folds
    // each packet's SnapDelta into its own replica (_applySnap), so the threads share no mutable map. Refreshed incrementally.
    private readonly Dictionary<IUIComponent, LayoutSnapshot> _snap = new();

    // APPLIER-owned replica, built ONLY from the deltas of packets it has consumed - what the draw pass actually reads.
    private readonly Dictionary<IUIComponent, LayoutSnapshot> _applySnap = new();

    // --- The recorder -> applier seam ---
    // DOUBLE-BUFFERED: the recorder fills one packet while the applier consumes another, so the loop no longer waits for
    // the GPU frame. Packets are DELTAS, so a queued one may never be DROPPED (a Partial carries only that frame's dirty
    // components) - the applier drains EVERY queued packet in order and draws ONCE, collapsing stale frames at the DRAW.
    private readonly ConcurrentQueue<RenderPacket> _published = new();   // recorded, awaiting the applier
    private readonly ConcurrentBag<RenderPacket> _spare = new();         // consumed, back for reuse
    private RenderPacket _packet;                                        // the one being recorded right now

    private readonly Dictionary<IUIComponent, float> _opacityChain = new();   // memo of OpacityChain, like _worldCache
    private readonly Dictionary<IUIComponent, int> _opacitySlotCache = new();   // memo of OpacitySlotOf, same lifetime
    private readonly Dictionary<IUIComponent, int> _fadeOwners = new();

    // Set when a fade slot is handed out for the first time - the instances beneath it still carry the old index, so the
    // frame has to walk instead of patch. Cleared by the walk that acts on it.
    private bool _fadeSlotJustCreated;

    // --- Motion-node memos (the O(1)-scroll path) ---
    // NodeOf: the nearest IsRenderMotionNode ancestor (or null). RelWorld: the transform RELATIVE to that node (identity
    // AT the node) - what a node-local bake uses; the shader applies the node's table matrix on top. Cleared with _worldCache.
    private readonly Dictionary<IUIComponent, IUIComponent> _nodeCache = new();
    private readonly Dictionary<IUIComponent, Matrix4x4F> _relWorldCache = new();
    private readonly Dictionary<IUIComponent, int> _nodeRefreshed = new();   // node -> walk version its slot was refreshed
    // Per RECORDING walk: node -> "every drawn unit under it is in a node-aware batch (rect/ellipse with its slot)". A
    // moved node with ANY non-aware content (world-baked text, per-unit draws) can't take the slot-write fast path -> the
    // frame falls back to the full walk.
    private readonly Dictionary<Guid, bool> _nodeAllAware = new();

    // The units this op stream left OUT because they were entirely outside their clip. They have no op in it, so nothing
    // can re-point them into view: a frame that moves one back inside has to walk. Lives exactly as long as the stream.
    private readonly HashSet<Core.Graphics.IRenderUnit> _culledWhenRecorded = new();
    // ...and which units cannot follow, so they are re-baked beside the node's slot write instead of refusing the frame.
    private readonly Dictionary<Guid, HashSet<IUIComponent>> _nodeStragglers = new();
    // APPLIER-owned: the moved nodes of the packets drained for THIS draw (it rewrites their table matrices, then clears).
    // The recorder must not read it - it takes the frame's moved nodes off RenderDirty into packet.MovedNodes.
    private readonly List<IUIComponent> _movedNodesBuf = new();
    // APPLIER-owned: this packet's movers whose change of PLACE the retained stream survives.
    private readonly HashSet<IUIComponent> _forgivenMoves = new();
    // ...and the subset whose change of SIZE it survives too - the ones with no clip under them to change shape.
    private readonly HashSet<IUIComponent> _forgivenResize = new();
    // The nodes whose matrices THIS frame rewrote - the replay re-points the per-unit draws under them.
    private readonly HashSet<IUIComponent> _movedNodeOwners = new();
    // Motion nodes that moved because a node ABOVE them did - they carry their own world, so they need writing too.
    private readonly List<IUIComponent> _nestedMovedNodes = new();
    // Motion nodes sitting inside an ORDINARY mover's subtree - their slots move with it, and nothing else writes them.
    private readonly List<IUIComponent> _movedNodesInSubtree = new();
    // APPLIER-owned: the ORDINARY movers of the packets drained for this draw (RefreshMovedComponents writes their
    // subtrees' slots, then clears). Filled only for movers the applier forgave.
    private readonly List<IUIComponent> _movedOwnersBuf = new();
    // The components THIS frame re-baked for a move - the replay re-points the per-unit draws among them.
    private readonly HashSet<IUIComponent> _movedOwners = new();
    private readonly List<IUIComponent> _movedSubtree = new();   // ...in visit order, so the re-bake is one flat pass
    // RECORDER-owned: the components that MOVED this frame - CaptureSnapshot re-freezes exactly their snapshot entries.
    private readonly List<IUIComponent> _movedBuf = new();
    private bool _snapFullCapture;   // adorner build only: re-capture the snapshot from the retained units

    // Frame-scoped clip memo. A unit's scissor = the intersection of every ClipToBounds ancestor's world-space viewport -
    // depends ONLY on the ancestor chain, so all units under one clipping ancestor share it (the old code re-walked per
    // unit). CumulativeClip(c) = (c clips ? c.worldRect : none) ∩ CumulativeClip(parent). Cleared each frame.
    private readonly Dictionary<IUIComponent, Rect?> _clipCache = new();

    // CPU pre-transform text batch aggregator. Lazy on the first Render with a device
    // (GPU-free test renders never batch). Frame-scoped state lives inside it.
    private TextBatchCollector _textBatch;

    /// <summary>Builds units from a FLAT list of components (the adorner stage), not a tree walk. Components cache by
    /// RenderId as in the tree build; units of components no longer in the list are disposed. For overlays outside the
    /// content tree.</summary>
    // readOnly: emit each component's commands via RenderReadOnly (no IsGeometryValid touch / no RenderDirty mark) instead
    // of Render() - for snapshotting a LIVE, already-valid subtree through this parallel cache without disturbing the
    // window (whose loop a mark would wake into a concurrent, hanging render). Adorners (the default) pass false.
    public void BuildFromComponents(IReadOnlyList<IUIComponent> components, Matrix4x4F projectionMatrix, bool readOnly = false)
    {
        // Overlays never run ApplyFrame, so late glyphs are adopted here.
        AdoptReadyGlyphs();

        // A FULL rebuild every call. Must record LastBuildKind=Full: the batches' Clean-frame upload-skip reads it, else
        // the overlay batch skips every GPU upload - its SSBO never fills and the whole overlay renders nothing.
        LastBuildKind = RenderBuildKind.Full;
        _commands.Clear();
        ClearOrder();
        _snap.Clear();         // full rebuild -> drop last frame's frozen layout snapshot (else stale overlay positions + unbounded _snap growth)
        _worldCache.Clear();   // new frame: drop last frame's transform + clip memos
        _clipCache.Clear();
        _clipOwnerCache.Clear();
        _clipSlotCache.Clear();
        _clipShapeCache.Clear();
        _relWorldCache.Clear();
        _nodeCache.Clear();

        // This build FUSES record and apply (an overlay renders a flat list straight into its groups - it never crosses
        // the render-thread seam), but the snapshot still flows through a packet - the only thing that fills the applier's
        // replica. Rent one, capture into it, fold it in, hand it back.
        _packet = RentPacket();
        _packet.Reset(RenderBuildKind.Full);
        _packet.SnapReset = true;

        var present = new HashSet<Guid>();
        if (components != null)
        {
            long order = 0;
            foreach (var component in components)
            {
                if (component.Visibility != Visibility.Visible) continue;
                present.Add(component.RenderId);

                var wasGeometryValid = component.IsGeometryValid;
                _drawingContextInternal.Clear();
                if (readOnly) 
                    component.RenderReadOnly(_drawingContext); 
                else 
                    component.Render(_drawingContext);
                ProcessRenderCommands(component, _drawingContextInternal.GetDrawCommands(), projectionMatrix, !readOnly && wasGeometryValid, order, component.RenderClones);
                order += OrderGap;   // the flat list IS the paint order
            }
        }

        // Free the units of any component dropped from the list since the last build.
        List<Guid> stale = null;
        foreach (var id in _groupById.Keys)
            if (!present.Contains(id)) (stale ??= new List<Guid>()).Add(id);
        if (stale != null)
            foreach (var id in stale) RemoveAndDeferDispose(id);

        // Freeze the overlay's snapshot. Its draws never went through a packet, so the capture reads the just-built GROUPS
        // - safe only here (this cache's recorder and applier are the same thread).
        _snapFullCapture = true;
        CaptureSnapshot();

        _applySnap.Clear();
        foreach (var entry in _packet.SnapDelta) _applySnap[entry.Key] = entry.Value;
        _packet.Reset(RenderBuildKind.Clean);
        _spare.Add(_packet);
        _packet = null;
    }

    /// <summary>The record half of an overlay build, device-free, on the thread that lays the components out: renders the
    /// flat list into a packet and freezes their layout with it, so <see cref="ApplyComponents"/> never reads a live
    /// component. <see cref="BuildFromComponents"/> does both halves at once.</summary>
    public void RecordComponents(IReadOnlyList<IUIComponent> components, Matrix4x4F projectionMatrix)
    {
        _packet = RentPacket();
        _packet.Reset(RenderBuildKind.Full);
        _packet.ProjectionMatrix = projectionMatrix;
        _packet.IsTransformDirty = true;
        _packet.ClearMemos = true;
        _packet.SnapReset = true;
        _snap.Clear();

        if (components != null)
        {
            long order = 0;
            foreach (var component in components)
            {
                if (component.Visibility != Visibility.Visible) continue;

                var wasGeometryValid = component.IsGeometryValid;
                _drawingContextInternal.Clear();
                component.Render(_drawingContext);
                var commands = CopyCommands(_drawingContextInternal.GetDrawCommands());
                _packet.Draws.Add(new ComponentDraw(component, commands, wasGeometryValid, order, component.RenderClones));
                order += OrderGap;
            }
        }

        foreach (var draw in _packet.Draws)
        {
            for (var c = draw.Component; c != null && !_snap.ContainsKey(c); c = c.RenderParent)
            {
                Snap(c);
            }
        }

        _published.Enqueue(_packet);
        _packet = null;
    }

    /// <summary>The apply half of an overlay build, on the thread that owns the device: realizes the recorded packets in
    /// order and frees the units of whatever the last one no longer lists. False when nothing was recorded since the
    /// last call.</summary>
    public bool ApplyComponents()
    {
        AdoptReadyGlyphs();

        var applied = false;
        while (_published.TryDequeue(out var packet))
        {
            ApplyComponentsPacket(packet);
            packet.Reset(RenderBuildKind.Clean);
            _spare.Add(packet);
            applied = true;
        }

        return applied;
    }

    private void ApplyComponentsPacket(RenderPacket packet)
    {
        LastBuildKind = RenderBuildKind.Full;
        _projectionMatrix = packet.ProjectionMatrix;
        _commands.Clear();
        ClearOrder();
        _worldCache.Clear();
        _clipCache.Clear();
        _clipOwnerCache.Clear();
        _clipSlotCache.Clear();
        _clipShapeCache.Clear();
        _relWorldCache.Clear();
        _nodeCache.Clear();

        _applySnap.Clear();
        foreach (var entry in packet.SnapDelta) _applySnap[entry.Key] = entry.Value;

        var present = new HashSet<Guid>();
        foreach (var draw in packet.Draws)
        {
            present.Add(draw.Component.RenderId);
            ProcessRenderCommands(draw.Component, draw.Commands, packet.ProjectionMatrix, draw.WasGeometryValid, draw.Order,
                draw.Clones);
        }

        List<Guid> stale = null;
        foreach (var id in _groupById.Keys)
        {
            if (!present.Contains(id)) (stale ??= new List<Guid>()).Add(id);
        }

        if (stale != null)
        {
            foreach (var id in stale) RemoveAndDeferDispose(id);
        }
    }

    /// <summary>Releases the batch rings and transform table for a closing window; <see cref="DisposeUnits"/> keeps them
    /// across designer re-renders.</summary>
    public void DisposeDeviceResources()
    {
        var device = _renderUnitFactory?.GraphicsDevice;
        if (device == null) return;

        _rectBatch?.DisposeGpuResources(device);
        _ellipseBatch?.DisposeGpuResources(device);
        _polygonBatch?.DisposeGpuResources(device);
        _textBatch?.DisposeGpuResources(device);
        _gradientRectBatch?.DisposeGpuResources(device);
        _gradientEllipseBatch?.DisposeGpuResources(device);
        _patternBatch?.DisposeGpuResources(device);
        _fractalBatch?.DisposeGpuResources(device);
        _texRectBatch?.DisposeGpuResources(device);
        _materialBatch?.DisposeGpuResources(device);
        _haloUnder?.DisposeGpuResources(device);
        _haloOver?.DisposeGpuResources(device);
        _haloLivingUnder?.DisposeGpuResources(device);
        _haloLivingOver?.DisposeGpuResources(device);
        _canvasGridBatch?.DisposeGpuResources(device);
        _inkBatch?.DisposeGpuResources(device);
        _arrowBatch?.DisposeGpuResources(device);
        _instancedFill?.Dispose();
        _transformTable?.Dispose();

        _rectBatch = null;
        _ellipseBatch = null;
        _polygonBatch = null;
        _textBatch = null;
        _gradientRectBatch = null;
        _gradientEllipseBatch = null;
        _patternBatch = null;
        _fractalBatch = null;
        _texRectBatch = null;
        _materialBatch = null;
        _haloUnder = null;
        _haloOver = null;
        _haloLivingUnder = null;
        _haloLivingOver = null;
        _canvasGridBatch = null;
        _inkBatch = null;
        _arrowBatch = null;
        _instancedFill = null;
        _transformTable = null;
    }

    /// <summary>Disposes every cached unit (GPU idle first); the off-screen designer resets with it between renders, since
    /// its controls never detach.</summary>
    public void DisposeUnits()
    {
        foreach (var group in _groupById.Values)
        {
            foreach (var unit in group.Units)
                unit?.Dispose();
        }

        _groupById.Clear();
        ClearOrder();
        _recordedUnits.Clear();   // the recorder's mirror of the above
        // The designer builds a new tree per render (fresh RenderIds), so the old frozen layout would grow unboundedly.
        _snap.Clear();
        _applySnap.Clear();
        // Nothing is built any more: left set, the next record read "no marks" as a clean frame and replayed the op stream
        // of the scene these units drew.
        _built = false;
        _clipOwners.Clear();
        _fadeOwners.Clear();
        _transformTable?.ReleaseAll();
    }

    /// <summary>The last packet's projection, captured by the RECORDER from the root visual. The applier uses this, not
    /// the live window (it may be the render thread).</summary>
    public Matrix4x4F AppliedProjection => _projectionMatrix;

    public void ProcessCommands(Matrix4x4F projectionMatrix, double renderScale)
    {
        _renderScale = renderScale;
        _projectionMatrix = projectionMatrix;
        foreach (var group in _groups)
        foreach (var unit in group.Units)
        {
            var transform = World(unit.Component);
            unit.Update(transform, projectionMatrix, renderScale);
        }
    }

    private RenderPacket RentPacket() => _spare.TryTake(out var packet) ? packet : new RenderPacket();

    /// <summary>Test hook: snapshot entries actually handed to the applier. It is the number a retained frame lives or
    /// dies by - the applier reads ANY entry as "the layout moved under the recorded stream" and refuses to replay it,
    /// so an idle frame publishing entries costs the whole scene its retained path (measured: 28 fps against ~320).</summary>
    internal static long SnapshotEntriesPublished { get; private set; }

    /// <summary>Test hook: transform-table slots held right now.</summary>
    internal int LiveTransformSlots => _transformTable?.LiveSlotCount ?? 0;

    private void PublishSnapshot(IUIComponent component, LayoutSnapshot snapshot)
    {
        SnapshotEntriesPublished++;
        Core.Diagnostics.RuntimeStats.LastSnapPublished++;
        _packet.SnapDelta.Add(new KeyValuePair<IUIComponent, LayoutSnapshot>(component, snapshot));
    }

    // Only a CLIP has rounded corners worth freezing - everything else paints its own and needs nothing here.
    private static Vector4F ClipRadiiOf(IUIComponent c) => c.ClipToBounds ? c.ClipRadii : Vector4F.Zero;

    // Record one component's frozen layout into the recorder's map AND this frame's delta (the applier's replica is built
    // from nothing else). Memoized: an unchanged component is captured once and never re-sent.
    private LayoutSnapshot Snap(IUIComponent c)
    {
        if (_snap.TryGetValue(c, out var s)) return s;
        s = new LayoutSnapshot(c.LocalTransform, c.RenderSize, c.ClipToBounds, c.IsRenderMotionNode, c.RenderParent,
            (float)c.Opacity, (float)c.SelfOpacity, ClipRadiiOf(c));
        if (c is Core.FundamentalUIComponent { IsDiscarded: true }) return s;
        _snap[c] = s;
        PublishSnapshot(c, s);
        return s;
    }

    // APPLIER's view: its private replica, folded from the packets' deltas. A miss (impossible for anything drawn) falls
    // back to the live component - the ONE live read left on this side, unreachable in the recorded paths.
    private LayoutSnapshot ApplySnap(IUIComponent c)
    {
        if (_applySnap.TryGetValue(c, out var s)) return s;
        s = new LayoutSnapshot(c.LocalTransform, c.RenderSize, c.ClipToBounds, c.IsRenderMotionNode, c.RenderParent,
            (float)c.Opacity, (float)c.SelfOpacity, ClipRadiiOf(c));

        // ...but a part the teardown DESTROYED is not cached. This miss-fallback is the third way into the map and the
        // one that kept re-adding what the sweep had just removed: 39 dead controls a swap survived both a sweep taking
        // 762 out and a guard on the packet path. Answer the caller, hold nothing.
        if (c is Core.FundamentalUIComponent { IsDiscarded: true }) return s;

        _applySnap[c] = s;
        return s;
    }

    // Freezes the layout of everything the draw will read at the end of the record, so the applier never touches a live
    // component. Incremental: only this frame's changes are re-frozen; a Full packet re-freezes all.
    private void CaptureSnapshot()
    {
        _refreshedThisCapture.Clear();

        if (_snapFullCapture)
        {
            foreach (var group in _groups)
            foreach (var unit in group.Units)
                for (var c = unit.Component; c != null && !_snap.ContainsKey(c); c = c.RenderParent)
                    Snap(c);
            _snapFullCapture = false;
        }

        // Element opacity lives IN the snapshot (unlike a brush recolor, which re-bakes from the brush BY REFERENCE), so a
        // paint-dirty component whose opacity ACTUALLY changed must re-publish its entry - on any build kind. Gated on a
        // real change so the common brush pulse (~470 cards/frame) re-freezes nothing.
        var opacityStart = System.Diagnostics.Stopwatch.GetTimestamp();
        Dirty.SnapshotPaintInto(_opacityCheckBuf);
        foreach (var c in _opacityCheckBuf)
            if (IsDrawn(c) && (!_snap.TryGetValue(c, out var f) || f.Opacity != (float)c.Opacity || f.SelfOpacity != (float)c.SelfOpacity))
                RefreshSnapshot(c);
        Core.Diagnostics.RuntimeStats.LastSnapOpacityMs = System.Diagnostics.Stopwatch.GetElapsedTime(opacityStart).TotalMilliseconds;

        if (_packet.Kind == RenderBuildKind.Full)
        {
            // A FULL walk re-records the whole scene to rebuild the paint ORDER - it does NOT mean the whole scene's LAYOUT
            // changed. Freeze only what a mark says changed, plus first-seen components (Snap fills those lazily).
            // Unnameable changes already cleared the snapshot in RecordFullFrame, so every entry below is genuinely new.
            foreach (var draw in _packet.Draws)
                for (var c = draw.Component; c != null && !_snap.ContainsKey(c); c = c.RenderParent)
                    Snap(c);

            foreach (var component in _geometryDirtyBuffer) RefreshSnapshot(component);   // size / clip may have changed
            foreach (var component in _structuralBuf) RefreshSnapshot(component);         // VisualParent may have changed
            foreach (var node in _movedNodesCapture) RefreshSnapshot(node);
            foreach (var moved in _movedBuf) RefreshSnapshot(moved);
            return;
        }

        // Re-recorded this frame (the dirty/newly-spliced components of a Partial or a Structural).
        var drawsStart = System.Diagnostics.Stopwatch.GetTimestamp();
        foreach (var draw in _packet.Draws) RefreshSnapshot(draw.Component);
        Core.Diagnostics.RuntimeStats.LastSnapDrawsMs = System.Diagnostics.Stopwatch.GetElapsedTime(drawsStart).TotalMilliseconds;
        var dirtyStart = System.Diagnostics.Stopwatch.GetTimestamp();

        // Every geometry-dirty drawn component, not only re-recorded ones: a size can change without a re-record. Components
        // no longer drawn are skipped so their dropped entries stay dropped.
        foreach (var component in _geometryDirtyBuffer)
        {
            // Cheapest question first: IsDrawn walks the ancestor chain, and most of this set was already re-frozen by
            // the packet's draws just above.
            if (_refreshedThisCapture.Contains(component)) continue;
            if (IsDrawn(component)) RefreshSnapshot(component);
        }
        Core.Diagnostics.RuntimeStats.LastSnapDirtyMs = System.Diagnostics.Stopwatch.GetElapsedTime(dirtyStart).TotalMilliseconds;

        var tailStart = System.Diagnostics.Stopwatch.GetTimestamp();

        // A component that kept its units but MOVED (VisualParent changed) - nothing else would re-freeze it.
        foreach (var rerank in _packet.Reranks) RefreshSnapshot(rerank.Key);
        // Moved this frame. A stale moved-MOTION-NODE entry is the classic O(1)-path regression: World composes it from
        // LAST frame's LocalTransform, so a tilting tile never moves and a flip sticks at its old angle (the angle lives
        // only here). Read off THIS frame's packet: _movedNodesBuf is the APPLIER's copy.
        foreach (var node in _packet.MovedNodes) RefreshSnapshot(node);
        foreach (var moved in _movedBuf) RefreshSnapshot(moved);
        Core.Diagnostics.RuntimeStats.LastSnapTailMs = System.Diagnostics.Stopwatch.GetElapsedTime(tailStart).TotalMilliseconds;
    }

    // Components already re-frozen this capture: the source sets overlap heavily, and a repeat can only recompute the
    // same value.
    private readonly HashSet<IUIComponent> _refreshedThisCapture = new();

    // Re-freezes one changed component's entry, then snaps its ancestors lazily.
    private void RefreshSnapshot(IUIComponent component)
    {
        if (component == null || !_refreshedThisCapture.Add(component)) return;
        if (component is Core.FundamentalUIComponent { IsDiscarded: true }) return;
        var snapshot = new LayoutSnapshot(component.LocalTransform, component.RenderSize, component.ClipToBounds,
            component.IsRenderMotionNode, component.RenderParent, (float)component.Opacity, (float)component.SelfOpacity,
            ClipRadiiOf(component));

        // Publish explicitly (the delta is the applier's only source), but only on a real change: any delta entry makes
        // the applier refuse the clean-frame replay.
        if (!_snap.TryGetValue(component, out var previous) || !previous.Equals(snapshot))
        {
            _snap[component] = snapshot;
            PublishSnapshot(component, snapshot);
        }

        for (var c = component.RenderParent; c != null && !_snap.ContainsKey(c); c = c.RenderParent)
            Snap(c);
    }

    private Matrix4x4F World(IUIComponent c)
    {
        if (_worldCache.TryGetValue(c, out var m)) return m;
        var s = ApplySnap(c);
        m = s.RenderParent != null ? s.LocalTransform * World(s.RenderParent) : s.LocalTransform;
        _worldCache[c] = m;
        return m;
    }

    /// <summary>Freezes <paramref name="element"/> at the origin with no parent, so an off-screen snapshot into an
    /// element-sized target shares one 0-based space. Call after BuildFromComponents, before ProcessCommands.</summary>
    public void RebaseToOrigin(IUIComponent element)
    {
        if (element == null) return;
        var s = ApplySnap(element);
        _applySnap[element] = new LayoutSnapshot(Matrix4x4F.Identity, s.RenderSize, s.ClipToBounds, false, null,
            s.Opacity, s.SelfOpacity, s.ClipRadii);
        _worldCache.Clear();    // drop any absolute transforms/clips memoized during the build so ProcessCommands recomputes rebased
        _relWorldCache.Clear();
        _clipCache.Clear();
        _clipOwnerCache.Clear();
        _clipSlotCache.Clear();
        _clipShapeCache.Clear();
        _nodeCache.Clear();
    }

    // Effective alpha the bake folds into a unit's color: SelfOpacity x the OPACITY chain (own Opacity x every
    // ancestor's). Reads ONLY the frozen snapshot - no live property, so no lock/box, render-thread safe
    // (see hot-paths-must-not-use-property-system). Cheaper than World (scalar mul, not matrix).
    private float EffectiveOpacity(IUIComponent c)
    {
        var s = ApplySnap(c);
        return s.SelfOpacity * OpacityChain(c);
    }

    private float OpacityChain(IUIComponent c)
    {
        if (_opacityChain.TryGetValue(c, out var v)) return v;
        var s = ApplySnap(c);
        v = s.RenderParent != null ? s.Opacity * OpacityChain(s.RenderParent) : s.Opacity;
        _opacityChain[c] = v;
        return v;
    }

    // The opacity slot of the nearest fading ancestor-or-self, so a fade is float writes, not a re-bake. Slots are kept
    // at 1.0, since animations pass through opaque constantly.
    private int OpacitySlotOf(IGraphicsDevice device, IUIComponent c)
    {
        if (c == null || _transformTable == null) return -1;
        if (_opacitySlotCache.TryGetValue(c, out var cached)) return cached;

        var s = ApplySnap(c);
        var parent = s.RenderParent != null ? OpacitySlotOf(device, s.RenderParent) : -1;

        int slot;
        var had = _transformTable.TryGetOpacitySlot(c.RenderId, out _);
        if (!had && s.Opacity >= 1f)
        {
            slot = parent;   // draws at its parent's alpha - no link of its own
        }
        else
        {
            // A new slot is structural: the subtree must be re-baked once to pick up the index.
            if (!had) _fadeSlotJustCreated = true;

            slot = _transformTable.AcquireOpacitySlot(c.RenderId);
            _fadeOwners[c] = slot;
            _transformTable.SetAlpha(device, slot, s.Opacity);
            _transformTable.SetOpacityParent(device, slot, parent);

            // Just allocated, and the buffer this frame draws from was sized BEFORE that: an instance carrying this
            // index would have the shader read past the allocation. Draw at the parent's alpha for one frame; the next
            // walk finds the slot live.
            if (!_transformTable.IsSlotLive(slot)) slot = parent;
        }

        _opacitySlotCache[c] = slot;
        return slot;
    }

    // This unit's family READS the element's alpha from its opacity slot, so its color must not carry the chain as
    // well - or the fade lands twice and the element comes out too dark. Called by those branches only; everything else
    // keeps the chain in its color.
    private void FadeBySlot(Core.Graphics.IRenderUnit u)
    {
        if (u.Component == null) return;

        u.SetEffectiveOpacity(ApplySnap(u.Component).SelfOpacity);
    }

    private IUIComponent NodeOf(IUIComponent c)
    {
        if (c == null) return null;
        if (_nodeCache.TryGetValue(c, out var n)) return n;
        var s = ApplySnap(c);
        n = s.IsMotionNode ? c : NodeOf(s.RenderParent);
        _nodeCache[c] = n;
        return n;
    }

    private Matrix4x4F RelWorld(IUIComponent c)
    {
        if (_relWorldCache.TryGetValue(c, out var m)) return m;
        var s = ApplySnap(c);
        m = s.IsMotionNode
            ? Matrix4x4F.Identity
            : (s.RenderParent is { } p ? s.LocalTransform * RelWorld(p) : s.LocalTransform);
        _relWorldCache[c] = m;
        return m;
    }

    // True when a matrix only scales and translates, so an axis-aligned rect stays one under it and the bake can fold it
    // into the instance's bounds. Rotation or shear puts numbers in M12/M21, and no axis-aligned rect can carry those.
    private static bool IsAxisAligned(in Matrix4x4F m)
    {
        const float eps = 1e-4f;   // same threshold the batch collectors use when they check a bake
        return Math.Abs(m.M12) <= eps && Math.Abs(m.M21) <= eps;
    }

    // A unit's bake transform + transform-table slot: node-local + the node's slot when under a motion node (matrix
    // refreshed once per walk), else its OWN slot holding the world. NOTHING is baked into an instance any more.
    // Where a unit's geometry is baked and which transform slot carries the rest; clones fold their offset into the bake,
    // so they ride the node's slot like the tiles.
    private Matrix4x4F ResolveBake(IGraphicsDevice device, IUIComponent component, Matrix4x4F world, out int slot)
    {
        var bake = ResolveBakeCore(device, component, world, out slot);
        return _cloneMatrix.HasValue ? bake * _cloneMatrix.Value : bake;
    }

    private Matrix4x4F ResolveBakeCore(IGraphicsDevice device, IUIComponent component, Matrix4x4F world, out int slot)
    {
        var node = NodeOf(component);
        if (node == null)
        {
            // The world always goes to the table, never into the instance, so there is one place a position lives.
            slot = _transformTable.AcquireSlot(component.RenderId);
            _transformTable.SetMatrix(device, slot, world);
            return Matrix4x4F.Identity;
        }
        var rel = RelWorld(component);
        // Rotated/sheared UNDER a motion node (a spinner inside a scrolling list): the node's slot carries the NODE's
        // matrix, and node-local bounds would have to carry the rotation - which an axis-aligned rect cannot. Give this
        // unit its own slot holding its FULL world instead, and tell the node it can no longer move everything by
        // writing its own slot (this unit must be re-baked when the node moves, exactly as the per-unit path it replaces).
        if (!IsAxisAligned(rel))
        {
            slot = _transformTable.AcquireSlot(component.RenderId);
            _transformTable.SetMatrix(device, slot, World(component));
            MarkNodeNotAware(component);
            return Matrix4x4F.Identity;
        }

        slot = _transformTable.AcquireSlot(node.RenderId);
        if (!_nodeRefreshed.TryGetValue(node, out var v) || v != _walkVersion)
        {
            _nodeRefreshed[node] = _walkVersion;
            _transformTable.SetMatrix(device, slot, World(node));

            // ...and every node ABOVE it carries this content too, through this one: an outer node's move takes this
            // node's slot with it. Without saying so, a view whose children are all nodes (a sliding view around a
            // scrolling list) has no unit of its own to vouch for it and refuses every move. TryAdd, never assignment:
            // a "no" recorded by MarkNodeNotAware - which now travels up the same chain - outranks it.
            for (var up = NodeOf(ApplySnap(node).RenderParent); up != null; up = NodeOf(ApplySnap(up).RenderParent))
                _nodeAllAware.TryAdd(up.RenderId, true);
        }
        _nodeAllAware.TryAdd(node.RenderId, true);
        return rel;
    }

    // Whether writing this node's matrix moves everything riding it. Answers propagate up the node chain both ways, and a
    // node with no answer refuses.
    private bool NodeCarriesItsContent(IUIComponent node) => _nodeAllAware.GetValueOrDefault(node.RenderId, false);

    // A unit under a motion node drew a path its slot matrix can't move (world-baked text, per-unit, gradient for now) ->
    // the node loses the slot-write fast path this frame (recorded per walk).
    private void MarkNodeNotAware(IUIComponent component)
    {
        var node = NodeOf(component);
        if (node == null) return;
        _nodeAllAware[node.RenderId] = false;
        NoteStraggler(node, component);

        // ...and every node ABOVE it: what this unit cannot follow, it cannot follow for any of them. Assignment, not
        // TryAdd - a "no" outranks the yes an inner node vouched with (see NodeCarriesItsContent).
        for (var up = NodeOf(ApplySnap(node).RenderParent); up != null; up = NodeOf(ApplySnap(up).RenderParent))
        {
            _nodeAllAware[up.RenderId] = false;
            NoteStraggler(up, component);
        }
    }

    private void NoteStraggler(IUIComponent node, IUIComponent component)
    {
        if (!_nodeStragglers.TryGetValue(node.RenderId, out var behind))
            _nodeStragglers[node.RenderId] = behind = new HashSet<IUIComponent>();
        behind.Add(component);
    }

    // Can this moved node be carried at all? Asked BEFORE anything is written, so a "no" costs the frame a walk and
    // never a half-updated table. A node that carries everything needs nothing; one that does not has to know its
    // stragglers by name (a node NOBODY answered for still refuses - see NodeCarriesItsContent) and every straggler's
    // batched units have to be re-bakeable in place.
    private bool CanCarryStragglers(IUIComponent node)
    {
        if (NodeCarriesItsContent(node)) return true;
        if (!_nodeStragglers.TryGetValue(node.RenderId, out var behind)) return false;

        foreach (var c in behind)
        {
            if (!_groupById.TryGetValue(c.RenderId, out var g)) continue;
            foreach (var u in g.Units)
                if (Drawing(u) && HoldsInstances(u) && !IsSlotPatchable(u)) return false;
        }
        return true;
    }

    // Carry them. A straggler holds its OWN slot with its FULL world (that is why it could not ride the node's), so
    // ResolveBake rewriting that slot is what moves it - and for a per-unit draw that is the whole job, since the
    // replay re-points those under a moved node (RepointIfItMoved). One that holds instances is re-baked too.
    private bool CarryStragglers(IGraphicsDevice device, IUIComponent node)
    {
        if (NodeCarriesItsContent(node) || !_nodeStragglers.TryGetValue(node.RenderId, out var behind)) return true;

        foreach (var c in behind)
        {
            if (!_groupById.TryGetValue(c.RenderId, out var g)) continue;
            foreach (var u in g.Units)
            {
                if (!Drawing(u)) continue;
                var bakeWorld = ResolveBake(device, u.Component, World(u.Component), out var slot);
                if (HoldsInstances(u) && !PatchSlot(device, u, bakeWorld, slot)) return false;
            }
        }
        return true;
    }

    // Apply the moved nodes' new matrices (64B each) before a replay-based draw; stale position memos drop and rebuild
    // lazily O(dirty). Returns false when ANY moved node has non-aware retained content - the caller full-walks.
    private bool RefreshMovedNodes(IGraphicsDevice device)
    {
        if (_movedNodesBuf.Count == 0)
        {
            _movedNodeOwners.Clear();   // nothing moved this frame - the replay re-points nothing
            return true;
        }
        // NESTED nodes: a node's slot holds its OWN world, so one that moved only because an ANCESTOR node did has to be
        // written too - nothing else writes it, and its whole subtree would stay behind (a list inside a view that
        // slides). Nodes are counted in ones per window, so this is a short list against an ancestor walk rather than a
        // scan of anything.
        _nestedMovedNodes.Clear();
        foreach (var known in _nodeRefreshed.Keys)
            if (!_movedNodesBuf.Contains(known) && IsUnder(known, _movedNodesBuf)) _nestedMovedNodes.Add(known);

        foreach (var node in _movedNodesBuf) if (!CanCarryStragglers(node)) return false;
        foreach (var node in _nestedMovedNodes) if (!CanCarryStragglers(node)) return false;
        // Drop only the world memos: _snap is already fresh, and a move inside a viewport never changes the clip memo.
        _worldCache.Clear();
        _relWorldCache.Clear();
        _movedNodeOwners.Clear();
        foreach (var node in _movedNodesBuf)
        {
            if (_transformTable.TryGetSlot(node.RenderId, out var slot))
                _transformTable.SetMatrix(device, slot, World(node));
            _movedNodeOwners.Add(node);   // the replay re-points the per-unit draws under them
        }
        foreach (var node in _nestedMovedNodes)
        {
            if (_transformTable.TryGetSlot(node.RenderId, out var slot))
                _transformTable.SetMatrix(device, slot, World(node));
            _movedNodeOwners.Add(node);
        }

        // ...and last, whatever could not ride those slots - AFTER the memo flush above, so every straggler is re-baked
        // from the world the node has NOW and not the one it was memoized at.
        foreach (var node in _movedNodesBuf) if (!CarryStragglers(device, node)) return false;
        foreach (var node in _nestedMovedNodes) if (!CarryStragglers(device, node)) return false;

        _movedNodesBuf.Clear();
        return true;
    }

    // The frame's window scissor, for the cull test in CollectMovedSubtree.
    private Adamantium.Vulkan.Core.Rect2D _cullScissor;

    // This frame's full scissor. The WALK is handed it as an argument, but a patch is not - and a patch re-bakes records
    // that carry a clip slot, so it has to be able to ask for one too.
    private Adamantium.Vulkan.Core.Rect2D _frameScissor;

    // Carries ordinary (non-node) movers by re-baking their subtree's drawn units via ResolveBake + PatchSlot, which also
    // works under motion nodes; O(subtree), not O(scene). False hands the frame to the walk.
    private bool RefreshMovedComponents(IGraphicsDevice device, Adamantium.Vulkan.Core.Rect2D fullScissor)
    {
        _cullScissor = fullScissor;

        if (_movedOwnersBuf.Count == 0)
        {
            _movedOwners.Clear();   // nothing moved this frame - the replay re-points nothing
            return true;
        }

        // Validate the WHOLE set before touching anything, as the dirty loop does: a refusal must cost the frame
        // nothing but the walk it was going to take anyway.
        _movedOwners.Clear();
        _movedSubtree.Clear();
        foreach (var mover in _movedOwnersBuf)
        {
            if (CollectMovedSubtree(mover)) continue;
            _movedOwners.Clear();
            _movedSubtree.Clear();
            return false;
        }

        // Motion nodes inside an ordinary mover's subtree announce no move, so their matrices are written here; checked
        // before any write, so a refusal costs only the walk.
        _movedNodesInSubtree.Clear();
        foreach (var c in _movedSubtree)
            if (ApplySnap(c).IsMotionNode) _movedNodesInSubtree.Add(c);
        foreach (var node in _movedNodesInSubtree) if (!CanCarryStragglers(node)) return false;

        // Positions moved -> the composed world memos are stale (same reasoning as RefreshMovedNodes; the clip memo is
        // deliberately kept - a mover that changes a viewport is structural).
        _worldCache.Clear();
        _relWorldCache.Clear();

        foreach (var node in _movedNodesInSubtree)
        {
            if (_transformTable.TryGetSlot(node.RenderId, out var nodeSlot))
                _transformTable.SetMatrix(device, nodeSlot, World(node));
            _movedNodeOwners.Add(node);   // the replay re-points the per-unit draws under them
        }

        foreach (var c in _movedSubtree)
        {
            if (!_groupById.TryGetValue(c.RenderId, out var g)) continue;
            foreach (var u in g.Units)
            {
                if (!HoldsInstances(u)) continue;   // a per-unit draw - the replay re-points it (RepointIfItMoved)
                var bakeWorld = ResolveBake(device, u.Component, World(u.Component), out var slot);
                if (!PatchSlot(device, u, bakeWorld, slot)) return false;
            }
        }

        // ...and whatever under those nodes cannot ride their slots, exactly as the node path carries its own.
        foreach (var node in _movedNodesInSubtree) if (!CarryStragglers(device, node)) return false;

        _movedOwnersBuf.Clear();
        return true;
    }

    // Everything in a moved subtree, collected once. The visited set doubles as the guard against re-walking: a
    // container and its children can BOTH be named movers (a panel re-arranging its rows), and without it overlapping
    // subtrees would cost the frame O(n^2).
    private bool CollectMovedSubtree(IUIComponent c)
    {
        if (c == null || !_movedOwners.Add(c)) return true;

        if (_groupById.TryGetValue(c.RenderId, out var g))
        {
            foreach (var u in g.Units)
            {
                if (!Drawing(u)) continue;

                // A unit culled now or when the stream was built needs the walk: a patch can neither remove its old draw
                // nor add one that was never recorded.
                ResolveScissor(u.Component, World(u.Component), _cullScissor, out _, out var culled);
                if (culled || _culledWhenRecorded.Contains(u)) return false;

                if (HoldsInstances(u) && !IsSlotPatchable(u)) return false;
            }
        }

        _movedSubtree.Add(c);
        foreach (var child in c.VisualChildren)
            if (!CollectMovedSubtree(child)) return false;

        return true;
    }

    // The clip slot a unit under this component must read, or -1. Memoized per frame like CumulativeClip, and for the
    // same reason: every unit under one clip asks the same question.
    private readonly Dictionary<IUIComponent, int> _clipSlotCache = new();

    // The same clip as a SHAPE, for the draws that cannot read the table by slot - filled by the walk below, in the one
    // place that already computes it, so the two can never disagree. See RenderData.RoundedClipBox.
    private readonly Dictionary<IUIComponent, (Vector4F Box, Vector4F Radii)> _clipShapeCache = new();

    // The clip OWNERS whose slots exist - kept across frames, unlike the cache above. A clip that changes shape (a radius
    // animating, the container resizing) must reach the screen on a REPLAYED frame too, and a replay re-records nothing:
    // the slot is the only thing that can carry it, so its contents are refreshed per frame from here.
    private readonly Dictionary<IUIComponent, int> _clipOwners = new();

    /// <summary>Rewrite every live clip slot from its owner's current shape. One 32-byte write per clip, and it is what
    /// makes a rounded clip follow a resize or an animated radius without the frame being re-recorded.</summary>
    private void RefreshClipSlots(Vulkan.Core.Rect2D fullScissor)
    {
        if (_clipOwners.Count == 0 || _transformTable == null) return;

        foreach (var (owner, slot) in _clipOwners)
        {
            var s = ApplySnap(owner);
            if (!s.ClipToBounds || s.ClipRadii == Vector4F.Zero) continue;

            var box = ToFramebufferScissor(new Rect(0, 0, s.RenderSize.Width, s.RenderSize.Height)
                .TransformToAABB(World(owner)), fullScissor);
            _transformTable.SetClip(null, slot,
                new Vector4F(box.Offset.X, box.Offset.Y, box.Extent.Width, box.Extent.Height),
                s.ClipRadii * (float)_renderScale);
        }
    }

    // The nearest rounded clip's slot only; outer ancestors still clip rectangularly through the scissor.
    private int RoundedClipSlot(IUIComponent c, Vulkan.Core.Rect2D fullScissor)
    {
        if (c == null || _transformTable == null) return -1;
        if (_clipSlotCache.TryGetValue(c, out var cached)) return cached;

        var slot = -1;
        for (var owner = c; owner != null; owner = ApplySnap(owner).RenderParent)
        {
            var s = ApplySnap(owner);
            if (!s.ClipToBounds || s.ClipRadii == Vector4F.Zero) continue;

            var box = ToFramebufferScissor(new Rect(0, 0, s.RenderSize.Width, s.RenderSize.Height)
                .TransformToAABB(World(owner)), fullScissor);

            var boxVec = new Vector4F(box.Offset.X, box.Offset.Y, box.Extent.Width, box.Extent.Height);
            var radiiVec = s.ClipRadii * (float)_renderScale;
            slot = _transformTable.AcquireClipSlot(owner.RenderId);
            _transformTable.SetClip(null, slot, boxVec, radiiVec);
            _clipShapeCache[c] = (boxVec, radiiVec);
            _clipOwners[owner] = slot;   // so a replayed frame can refresh it - see RefreshClipSlots
            break;
        }

        // A slot the shader cannot reach yet (the table grew past this frame's buffer) would be indexed out of the
        // allocation, and this device answers that with a lost device rather than a wrong pixel.
        if (slot >= 0 && !_transformTable.IsSlotLive(slot)) slot = -1;

        _clipSlotCache[c] = slot;
        return slot;
    }

    /// <summary>The rounded clip as a SHAPE (device px), for a per-unit draw that takes it as a uniform instead of
    /// reading the table by slot. Zero size = no clip. Asks the slot walk above so both answers come from one place.
    /// A slot the shader could not reach also answers "no clip" here, exactly as it does there.</summary>
    private (Vector4F Box, Vector4F Radii) RoundedClipShape(IUIComponent c, Vulkan.Core.Rect2D fullScissor)
    {
        var slot = RoundedClipSlot(c, fullScissor);
        return slot >= 0 && _clipShapeCache.TryGetValue(c, out var shape) ? shape : default;
    }

    // The nearest ClipToBounds ancestor-or-self, or null. Segments group by this owner, not by the resulting rect, since
    // a replay re-derives the scissor from its one owner.
    private IUIComponent ClipOwnerOf(IUIComponent c)
    {
        if (c == null) return null;
        if (_clipOwnerCache.TryGetValue(c, out var cached)) return cached;

        var s = ApplySnap(c);
        // An adorner takes its clip from the viewports above its TARGET (see AdornerClip), which no single ancestor
        // names - so it is its own group rather than being merged with anything.
        var owner = s.ClipToBounds ? c
            : !c.ClippedByRenderParent && s.RenderParent != null ? c
            : ClipOwnerOf(s.RenderParent);

        _clipOwnerCache[c] = owner;
        return owner;
    }

    private readonly Dictionary<IUIComponent, IUIComponent> _clipOwnerCache = new();

    private Rect? CumulativeClip(IUIComponent c)
    {
        if (c == null) return null;
        if (_clipCache.TryGetValue(c, out var cached)) return cached;
        var s = ApplySnap(c);
        // An adorner skips its TARGET's own clip and, above it, obeys only the VIEWPORTS - see ClippedByRenderParent.
        var parentClip = c.ClippedByRenderParent || s.RenderParent == null
            ? CumulativeClip(s.RenderParent)
            : AdornerClip(ApplySnap(s.RenderParent).RenderParent, c);
        var result = parentClip;
        if (s.ClipToBounds)
        {
            var rect = new Rect(0, 0, s.RenderSize.Width, s.RenderSize.Height).TransformToAABB(World(c));
            result = parentClip is { } p ? p.Intersect(rect) : rect;
        }
        _clipCache[c] = result;
        return result;
    }

    // An adorner's clip comes only from ancestors with IUIComponent.ClipsAdorners, not every ClipToBounds box; not
    // memoized, adorners are few.
    private Rect? AdornerClip(IUIComponent node, IUIComponent adorner)
    {
        // The viewport is widened by what the adorner is entitled to draw outside its target: otherwise a control
        // standing flush against the edge of a scroll area wears a shaved ring, which is exactly what the standoff
        // exists to avoid. Bounded by the standoff itself, so a row scrolled further than that is still cut.
        var standoff = (adorner as Controls.Adorners.Adorner)?.ClipStandoff ?? 0;

        Rect? result = null;
        for (var n = node; n != null; n = ApplySnap(n).RenderParent)
        {
            var s = ApplySnap(n);
            if (!s.ClipToBounds || !n.ClipsAdorners) continue;

            var rect = new Rect(0, 0, s.RenderSize.Width, s.RenderSize.Height).TransformToAABB(World(n));
            if (standoff > 0)
            {
                rect = new Rect(rect.X - standoff, rect.Y - standoff,
                    rect.Width + standoff * 2, rect.Height + standoff * 2);
            }

            result = result is { } r ? r.Intersect(rect) : rect;
        }

        return result;
    }
}


