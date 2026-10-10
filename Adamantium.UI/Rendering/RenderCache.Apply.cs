using System.Collections.Generic;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Rendering.Payloads;

namespace Adamantium.UI.Rendering;

public partial class RenderCache
{
    // WarmTextAtlases: one batched glyph rasterization per atlas.
    private readonly Dictionary<Adamantium.Graphics.Fonts.FontAtlas, List<(Adamantium.Fonts.IFont Font, Adamantium.Fonts.Glyph Glyph)>> _warmGlyphs = new();

    private readonly List<ControlGroup> _pendingInserts = new();   // ApplyStructural: groups to place, merged into the order once
    private readonly HashSet<ControlGroup> _pendingSet = new();
    private readonly List<ControlGroup> _mergedGroups = new();

    /// <summary>APPLY half of the frame build (GPU / render thread): consumes the recorder's packets - Clean re-draws the
    /// retained units, Partial updates dirty groups in place, Structural splices count changes, Full rebuilds the
    /// paint-order groups. Freezes the layout snapshot the draw pass replays. (RenderDirty is cleared ONCE per frame after
    /// every window has recorded - see below - not here.)</summary>
    public void ApplyFrame()
    {
        BeginApplyFrame();

        AdoptReadyGlyphs();   // glyphs that finished rasterizing since the last frame land here

        while (_published.TryDequeue(out var packet))
        {
            Observer?.Applied(packet);
            ApplyPacket(packet);
            packet.Reset(RenderBuildKind.Clean);
            _spare.Add(packet);   // back to the pool for the recorder
        }

        // RenderDirty (a GLOBAL set shared by every window) is NOT cleared per-window here: with two windows the first to
        // apply would wipe the set before the second records, so the second never re-records its content. Both the
        // single-threaded and the decoupled path now clear ONCE after ALL windows have recorded (single-threaded in
        // UIApplication.DispatchRenderFrame after ExecuteDrawSequence; decoupled in RecordRenderFrame).
    }

    /// <summary>Resets the per-DRAW merged apply state - the build kind, dirty set and moved nodes accumulate across every
    /// packet applied for this draw, so they must start empty.</summary>
    private void BeginApplyFrame()
    {
        LastBuildKind = RenderBuildKind.Clean;
        LastBuildTransformDirty = false;
        _partialDirty.Clear();
        _movedNodesBuf.Clear();
        _movedOwnersBuf.Clear();
        _partialSpliced = false;
    }

    // Warm every text block's atlas in ONE batch per atlas. Glyph rasterization is parallel MSDF work, but a text unit
    // built one at a time can only hand it ITS block's glyphs, so a cold fill rasterized ~50 blocks' glyphs serially
    // (1.1 s of a 1.9 s 4K fill). Pooling the whole packet's glyphs lets the generator spread them across cores.
    // Still lazy - only glyphs the UI actually shows are rasterized, each with the font that draws it.
    private void WarmTextAtlases(RenderPacket packet)
    {
        var device = _renderUnitFactory.GraphicsDevice;
        if (device == null || packet.Draws.Count == 0) return;

        _warmGlyphs.Clear();
        foreach (var draw in packet.Draws)
        foreach (var command in draw.Commands)
        {
            if (command.Payload is not TextPayload { TextLayout: { } layout } || string.IsNullOrEmpty(layout.Text)) continue;
            var atlas = layout.EnsureAtlas(device);
            if (!_warmGlyphs.TryGetValue(atlas, out var glyphs))
            {
                glyphs = [];
                _warmGlyphs[atlas] = glyphs;
            }

            glyphs.AddRange(layout.GetGlyphs());
        }

        // ASKED for, not waited on: the batch goes to a worker and this frame goes out with whatever the atlas
        // already holds. Pooling the packet's glyphs still matters - the generator parallelizes across the glyphs it is
        // handed, so one batch keeps every core busy where fifty single-glyph requests would not.
        foreach (var pair in _warmGlyphs) pair.Key.RequestAsync(pair.Value);
    }

    // The glyph-arrival version this cache has adopted; per cache, since arrival is global and adoption is not.
    private int _seenGlyphVersion;

    // Adopts glyphs the workers finished and rebuilds text built before its letters arrived.
    private void AdoptReadyGlyphs()
    {
        if (_renderUnitFactory.GraphicsDevice == null) return;

        // Pump drains a shared queue that only the first caller sees, so each cache compares its own version instead.
        Adamantium.Graphics.Fonts.FontAtlasStore.PumpReadyGlyphs();

        var landedVersion = Adamantium.Graphics.Fonts.FontAtlasStore.LandedVersion;
        if (landedVersion == _seenGlyphVersion) return;
        _seenGlyphVersion = landedVersion;

        var arrived = false;
        foreach (var group in _groups)
        foreach (var unit in group.Units)
        {
            if (unit is RenderUnits.TextRenderUnit text) arrived |= text.RefreshGlyphsIfArrived();
        }

        // Nobody marks arrived glyphs dirty (marks belong to the loop thread), so stale the stream here and request a frame.
        if (!arrived) return;

        StreamStaleBecause("glyphsArrived");
        Core.LoopSignal.Request();
    }

    // What the recorded op stream actually baked out of a snapshot: where the element is, how big it is, what clips it.
    // Opacity is NOT among them - it is re-composed per unit on every patch - so a fade must not cost a re-record.
    private static bool SameGeometry(LayoutSnapshot a, LayoutSnapshot b) =>
        a.LocalTransform == b.LocalTransform
        && a.RenderSize == b.RenderSize
        && a.ClipToBounds == b.ClipToBounds
        && a.IsMotionNode == b.IsMotionNode
        && ReferenceEquals(a.RenderParent, b.RenderParent);

    // The stream survives a move (slot writes carry it) and a resize only if the element is re-baked this frame and
    // nothing under it clips; any other snapshot change needs a re-record.
    private bool StreamSurvives(IUIComponent c, LayoutSnapshot was, LayoutSnapshot now) =>
        _forgivenMoves.Contains(c)
        && was.ClipToBounds == now.ClipToBounds
        && was.IsMotionNode == now.IsMotionNode
        && ReferenceEquals(was.RenderParent, now.RenderParent)
        && (was.RenderSize == now.RenderSize || (_rebakedThisPacket.Contains(c) && _forgivenResize.Contains(c)));

    // The components this packet's patch will re-bake - packet.PartialDirty, as a set (StreamSurvives asks per entry).
    private readonly HashSet<IUIComponent> _rebakedThisPacket = new();

    /// <summary>Index of the first group ranked AFTER <paramref name="order"/>, by bisection - `_groups` is kept sorted by
    /// paint rank, so nothing needs to be scanned to find a place in it.</summary>
    private int FirstGroupAfter(long order)
    {
        int low = 0, high = _groups.Count;
        while (low < high)
        {
            var mid = (low + high) >> 1;
            if (_groups[mid].Order > order) high = mid;
            else low = mid + 1;
        }

        return low;
    }

    // TEMP: name what staled the stream, so why=4 says which of its causes it was.
    private void StreamStaleBecause(string reason)
    {
        _layoutChangedSinceRecord = true;
        if (Core.Diagnostics.FrameTrace.Enabled) Core.Diagnostics.FrameTrace.LayoutChangedBy = reason;
    }

    // Realize ONE packet. The per-frame results the draw pass reads are MERGED across the packets drained this frame: a
    // Full supersedes everything before it; two Partials union their dirty sets.
    private void ApplyPacket(RenderPacket packet)
    {
        WarmTextAtlases(packet);   // one batched glyph rasterization for the whole packet, before any unit is built

        _projectionMatrix = packet.ProjectionMatrix;

        // The applier's OWN derived memos (world/clip/node transforms), dropped here not by the recorder: they are
        // applier-resident, and the recorder only says WHEN they went stale.
        if (packet.ClearMemos)
        {
            _worldCache.Clear();
            _clipCache.Clear();
            _clipSlotCache.Clear();
            _clipShapeCache.Clear();
            _relWorldCache.Clear();
            _nodeCache.Clear();
        }

        // WHICH viewport cuts an element is derived from the snapshot this packet is about to change - the ClipToBounds
        // flags and the render parent chain - so it is dropped on every packet, and not on every frame: nothing about it
        // depends on where anything moved to.
        _clipOwnerCache.Clear();

        // Fold this packet's layout delta into the applier's snapshot replica - the only thing the draw pass reads for a
        // component's transform/size/clip. A full walk resets it and carries the whole scene.
        if (packet.SnapReset) _applySnap.Clear();

        // Layout changes invalidate the retained stream unless a patch can carry them: moves of nodes and components are
        // slot writes and scissors are re-derived (RefreshMovedScissors); a resize under a clip cannot add culled draws.
        // Re-published entries are compared with what the stream baked, not taken as movement.
        _forgivenMoves.Clear();
        _forgivenResize.Clear();
        foreach (var node in packet.MovedNodes)
        {
            _forgivenMoves.Add(node);
            if (!SubtreeClips(node)) _forgivenResize.Add(node);
        }

        // Ordinary movers are forgiven too, their subtree's slots written one by one (RefreshMovedComponents); a mover
        // with no owner forgives nothing.
        _rebakedThisPacket.Clear();
        foreach (var dirty in packet.PartialDirty) _rebakedThisPacket.Add(dirty);

        var moversCarried = !packet.TransformUnknown;

        // Clipping movers are forgiven as well; CollectMovedSubtree refuses when a culled unit would need a new draw.
        // Resizes follow stricter terms (_forgivenResize).
        foreach (var mover in packet.Moved)
        {
            _forgivenMoves.Add(mover);
            if (!SubtreeClips(mover)) _forgivenResize.Add(mover);
        }

        foreach (var entry in packet.SnapDelta)
        {
            // A part the template teardown has DESTROYED gets no snapshot. Sweeping the map is not enough on its own:
            // the sweep runs mid-swap and the applier then writes the delta straight back in, so 39 dead controls a swap
            // survived a sweep that was removing 762. Nobody will ever draw these, and the key is the control itself.
            if (entry.Key is Core.FundamentalUIComponent { IsDiscarded: true })
            {
                _applySnap.Remove(entry.Key);
                continue;
            }

            var known = _applySnap.TryGetValue(entry.Key, out var previous);
            if (!known || !SameGeometry(previous, entry.Value))
            {
                if (!known || !StreamSurvives(entry.Key, previous, entry.Value))
                    StreamStaleBecause(known ? $"moved<{entry.Key.GetType().Name}>" : $"new<{entry.Key.GetType().Name}>");
            }
            _applySnap[entry.Key] = entry.Value;
        }

        // Something left the tree since the last build. Withdrawing what it drew is the reconcile's job, and it used to
        // ride on a FULL walk - which the redesign made rare, so a detached view kept its place in the order and the
        // retained op stream went on re-issuing it, frozen at the size it had when it left.
        if (_reconciledDetachGen != Dirty.DetachGeneration && packet.Kind != RenderBuildKind.Full)
        {
            _reconciledDetachGen = Dirty.DetachGeneration;
            if (ReconcileDetachedControls() > 0)
            {
                // Those units are gone, so the op stream and the recorded slots no longer describe the scene: the draw
                // pass must re-walk instead of replaying, exactly as after a splice.
                if (LastBuildKind != RenderBuildKind.Full) LastBuildKind = RenderBuildKind.Structural;
                _partialDirty.Clear();
                _partialSpliced = false;
            }
        }


        switch (packet.Kind)
        {
            case RenderBuildKind.Clean:
                // Nothing to realize - but a node that MOVED still has to have its matrix written before the replay, or
                // the frame draws the subtree where it was last recorded.
                _movedNodesBuf.AddRange(packet.MovedNodes);
                break;

            case RenderBuildKind.Partial:
            {
                // APPLY pass (GPU): realize the recorded draws - update the units in place / splice a count change.
                foreach (var draw in packet.Draws)
                    ApplyReRender(draw.Component, draw.Commands, draw.Order, draw.Clones);

                if (LastBuildKind != RenderBuildKind.Full) LastBuildKind = RenderBuildKind.Partial;
                _partialDirty.AddRange(packet.PartialDirty);
                _movedNodesBuf.AddRange(packet.MovedNodes);

                // A move only forbids the patch when nobody is going to carry it. When every mover is named and clip-free
                // the draw writes their subtrees' slots instead, so the flag - which is frame-wide, and therefore speaks
                // for 22k nodes when one thumb moved - stays down.
                LastBuildTransformDirty |= packet.IsTransformDirty && !moversCarried;
                if (moversCarried) _movedOwnersBuf.AddRange(packet.Moved);
                break;
            }

            case RenderBuildKind.Structural:
            {
                // Arrivals splice in their own segment; departures still need a walk, since the splice cannot reach ops of
                // a group that is gone. A renumber keeps relative order, so it is not a reorder.
                var reordered = packet.Reranks.Count > 0 && !packet.Renumbered;
                var local = packet.Removed.Count == 0 && packet.Undrawn.Count == 0
                            && !reordered && !packet.SnapReset && !_layoutChangedSinceRecord;


                ApplyStructural(packet);
                if (LastBuildKind != RenderBuildKind.Full)
                    LastBuildKind = local ? RenderBuildKind.Partial : RenderBuildKind.Structural;
                LastBuildTransformDirty = !local;
                _partialDirty.Clear();
                if (local)
                {
                    foreach (var draw in packet.Draws) _partialDirty.Add(draw.Component);
                    _partialSpliced = true;
                }
                else
                {
                    _partialSpliced = false;
                }

                _movedNodesBuf.AddRange(packet.MovedNodes);
                break;
            }

            case RenderBuildKind.Full:
                ApplyFullWalk(packet);   // GPU: rebuild the paint-order groups from the packet (reconciles as it goes)
                _reconciledDetachGen = Dirty.DetachGeneration;
                _built = true;
                // A full walk re-records the whole scene, so earlier packets' dirty entries are covered - and their unit
                // sets are gone (groups rebuilt), which would mis-patch the batch. Drop them.
                LastBuildKind = RenderBuildKind.Full;
                LastBuildTransformDirty = true;
                _partialDirty.Clear();
                _movedNodesBuf.Clear();
                _movedOwnersBuf.Clear();
                _partialSpliced = false;
                break;
        }
    }

    // The record+apply decision for one dirty component: Skip = reuse its cached units as-is (nothing recorded);
    // Fallback = the caller must do a full walk; Recorded = its commands were captured into the packet for the applier;
    // Undrawn = it is HIDDEN and keeps its place in the paint order, so it records ZERO commands (see RecordReRender).
    private enum PartialRecord { Skip, Fallback, Recorded, Undrawn }

    // The DECISION for one geometry-dirty component - PURE: it reads state and renders nothing, so the structural pass can
    // pre-validate the frame before it commits to anything (see RecordStructuralFrame).
    private PartialRecord ClassifyReRender(IUIComponent component)
    {
        // COLLAPSED - out of the layout as well as out of the frame: draws NOTHING and nothing to record. Its units are
        // retained and its dirty flag stays set until it is shown again, so it re-records at the right time (the
        // structural splice that puts it back), not now. A full walk here meant a whole-tree re-record for every
        // collapsed container that so much as re-bound.
        if (component.Visibility == Visibility.Collapsed) return PartialRecord.Skip;

        // Not in the live paint tree: DETACHED, or hidden by a COLLAPSED ancestor. The full walk never reaches it, so it
        // has no rank and draws nothing - yet it used to force a full rebuild EVERY dirty frame. Skip: it holds no units
        // (a real detach/collapse is STRUCTURAL and already removed them).
        if (!component.IsAttachedToVisualTree) return PartialRecord.Skip;

        var hidden = component.Visibility == Visibility.Hidden;
        for (var a = component.VisualParent; a != null; a = a.VisualParent)
        {
            if (a.Visibility == Visibility.Collapsed) return PartialRecord.Skip;
            if (a.Visibility == Visibility.Hidden) hidden = true;
        }

        // HIDDEN (itself, or under something hidden): it holds its slot and its rank and simply paints nothing. Saying so
        // - recording zero commands - empties its group in place, which is a count change the retained frame patches.
        // Without a rank there is nothing to patch INTO (a full walk while it was hidden never gave it one), and the
        // structural path has to put it back.
        if (hidden) return HasRank(component) ? PartialRecord.Undrawn : PartialRecord.Skip;

        // Components of foreign trees (popups, tooltips) never arrive here: RenderDirtyRouter routes marks per surface.

        // Marked dirty EXTERNALLY (an animation heartbeat) while its own geometry is still VALID: Render() would no-op and
        // record ZERO commands, read as "now draws nothing" -> the units get DELETED (the mass tile vanish on ease-back).
        // Its recorded geometry is unchanged - keep the units as-is.
        if (component.IsGeometryValid) return PartialRecord.Skip;

        // A relaid-out element that draws nothing re-records nothing; its children and snapshot are handled separately, and
        // content changes still arrive as content invalidations.
        if (component.DrawsNothing && !component.GeometryStaleByContent) return PartialRecord.Skip;

        // No paint rank: invisible/absent when the order was last derived, now appearing with no structural mark to place
        // it (an auto-hide ScrollBar fading in). Hand to a full walk. A component that DRAWS always has a rank, so this is
        // the appearing-content case only.
        if (!HasRank(component)) return PartialRecord.Fallback;

        return PartialRecord.Recorded;
    }

    // RECORD half of a partial re-render for ONE component (DEVICE-FREE): decide, then component.Render and copy the
    // commands into the packet. No GPU - the applier (ApplyReRender) realizes them.
    private PartialRecord RecordReRender(IUIComponent component, RenderPacket packet) =>
        RecordReRender(component, packet, ClassifyReRender(component));

    /// <summary>...with the decision already taken. The structural pass PRE-VALIDATES the whole dirty set before it
    /// commits to anything, so classifying each component again here was the same ancestor walk done twice per frame -
    /// 10000 components' worth on a tile drag.</summary>
    private PartialRecord RecordReRender(IUIComponent component, RenderPacket packet, PartialRecord decision)
    {
        if (decision is PartialRecord.Skip or PartialRecord.Fallback)
        {
            Core.Diagnostics.RuntimeStats.LastRecordClassifySkips++;
            return decision;
        }

        var rank = RankOf(component);
        _drawingContextInternal.Clear();
        var renderBytes0 = System.GC.GetAllocatedBytesForCurrentThread();
        var renderStart = System.Diagnostics.Stopwatch.GetTimestamp();
        component.Render(_drawingContext);   // NB: consumes the dirty flag (Render sets IsGeometryValid back to true)
        var renderMs = System.Diagnostics.Stopwatch.GetElapsedTime(renderStart).TotalMilliseconds;
        Core.Diagnostics.RuntimeStats.LastRecordRenderMs += renderMs;
        Core.Diagnostics.RuntimeStats.NoteRecordMs(component.GetType(), renderMs);
        // A HIDDEN element is rendered and its commands DROPPED, rather than not rendered at all: rendering is what
        // consumes the dirty flag, and an element that stays dirty is re-recorded every frame forever. What it says it
        // would draw is simply not what it draws while hidden.
        Core.Diagnostics.RuntimeStats.LastRecordRenderBytes += System.GC.GetAllocatedBytesForCurrentThread() - renderBytes0;
        var copyBytes0 = System.GC.GetAllocatedBytesForCurrentThread();
        var copyStart = System.Diagnostics.Stopwatch.GetTimestamp();
        var commands = decision == PartialRecord.Undrawn
            ? CopyCommands(System.Array.Empty<IDrawCommand>())
            : CopyCommands(_drawingContextInternal.GetDrawCommands());
        packet.Draws.Add(new ComponentDraw(component, commands, false, rank, component.RenderClones));
        Core.Diagnostics.RuntimeStats.LastRecordCopyMs += System.Diagnostics.Stopwatch.GetElapsedTime(copyStart).TotalMilliseconds;
        Core.Diagnostics.RuntimeStats.LastRecordCopyBytes += System.GC.GetAllocatedBytesForCurrentThread() - copyBytes0;
        // The one place that can know it: the record that just counted the commands. NOT on the Undrawn path - those
        // commands were dropped because it is hidden, which says nothing about what it draws when shown.
        if (decision != PartialRecord.Undrawn) component.DrawsNothing = commands.Count == 0;   // it was geometry-INVALID, so Render really ran
        if (commands.Count == 0)
        {
            Core.Diagnostics.RuntimeStats.LastRecordEmptyDraws++;
        }
        MirrorUnits(component, commands.Count, false);   // it WAS dirty: no commands now means "draws nothing" -> units freed
        return PartialRecord.Recorded;
    }

    // APPLY half (GPU): realize ONE recorded partial draw - update the group's units in place (same count+type) or splice
    // in the count/type change. `order` (the paint rank) rides WITH the draw, so a group appearing for the first time is
    // placed without the applier ever reading the recorder's rank map.
    private void ApplyReRender(IUIComponent component, IReadOnlyList<IDrawCommand> drawCommands, long order,
        IReadOnlyList<Adamantium.Mathematics.Matrix4x4F> clones = null)
    {
        _groupById.TryGetValue(component.RenderId, out var group);

        // The clone set travels with the contribution on EVERY path, this one included: a partial re-render that left it
        // untouched went on drawing the previous frame's set (caught by DroppingTheClones_ReturnsToASingleDraw).
        if (group != null) group.Clones = clones;
        var oldCount = group?.Units.Count ?? 0;

        // Fast path: same command count and every unit still matches -> update in place; nothing structural changed. Gate
        // on InOrder: a group that fell OUT of the paint order (its container was hidden/parked, then rebound and re-drawn
        // here) MUST be re-inserted, not just patched in place - so let it fall through to the re-insert below.
        if (group is { InOrder: true } && drawCommands.Count == oldCount && oldCount > 0)
        {
            var units = group.Units;
            var allMatch = true;
            for (var i = 0; i < drawCommands.Count; i++)
            {
                drawCommands[i].RenderData.ProjectionMatrix = _projectionMatrix;
                if (!units[i].Match(drawCommands[i])) { allMatch = false; break; }   // payload type changed
            }
            if (allMatch)
            {
                for (var i = 0; i < drawCommands.Count; i++) units[i].UpdateWithDrawCommand(drawCommands[i]);
                group.Order = order;
                return;
            }
        }

        // Count/type changed. The change stays LOCAL to this control's group (BuildUnitsFor refreshes its Units in place,
        // no other group moves). The recorded op stream + rect-slot map still reference the old unit set, so the draw
        // phase re-walks this frame.
        _partialSpliced = true;
        // (Re)insert into the paint order when the group is NEW *or* exists but has fallen OUT of the order - the same
        // check ApplyStructural makes. Without the InOrder half, a container that was hidden (its group left the order,
        // units kept) then rebound and re-recorded here got its units rebuilt but was never put back in _groups, so it
        // drew NOWHERE: the "dead" selection/hover highlight on a scrolled-then-returned list row (recycled container).
        var needsInsert = group is not { InOrder: true };

        group = BuildUnitsFor(component, drawCommands, _projectionMatrix);
        group.Order = order;

        if (needsInsert)
        {
            // Insert by paint rank, before the first group that ranks after it. Existing groups never move; the rank came
            // WITH the draw. Found by BISECTION, not by a scan: the list is kept sorted by rank, and a scan from the front
            // is O(scene) for every control that starts drawing - which, now that an arrival is patched instead of walked,
            // happens on the cheap path where a scene-sized loop has no business being.
            _groups.Insert(FirstGroupAfter(order), group);
            group.InOrder = true;
        }
    }

    // APPLY half of a STRUCTURAL frame (GPU): free what left, realize what arrived, and re-sort the paint order - all
    // O(changed) plus one linear merge, instead of rebuilding every group from a full walk.
    private void ApplyStructural(RenderPacket packet)
    {
        // Both departure loops leave the paint order in ONE pass - see RemoveFromOrder. A whole view leaving names its
        // entire realized subtree in a single packet, and removing those one at a time is quadratic in the scene.
        _batchOrderRemovals = true;

        // 1. DETACHED: gone for good - free its units.
        foreach (var component in packet.Removed)
        {
            RemoveAndDeferDispose(component.RenderId);
            _applySnap.Remove(component);
        }

        // 2. HIDDEN: it stops DRAWING, and that is all. Its group + units survive, so a re-show (a recycled container a
        //    few rows later) re-inserts a ready group instead of rebuilding buffers.
        foreach (var component in packet.Undrawn)
        {
            if (_groupById.TryGetValue(component.RenderId, out var hidden)) RemoveFromOrder(hidden, "undrawn");
            _applySnap.Remove(component);
        }

        FlushOrderRemovals();

        // Re-armed for the re-ranking loops below, so their removals batch into one pass; flushed before the order is read.
        _batchOrderRemovals = true;

        // 3. What ARRIVED (or re-recorded): build/refresh its units. Groups to place are collected for ONE merge below (a
        //    linear scan per insert would be O(new x scene) on a fill).
        _pendingInserts.Clear();
        _pendingSet.Clear();
        foreach (var draw in packet.Draws)
        {
            if (draw.Commands.Count == 0)
            {
                // Recorded nothing: clean keeps what it drew; dirty now draws nothing, so empty the group but keep its rank
                // so a return is a refill, not a re-insert.
                if (!draw.WasGeometryValid && _groupById.TryGetValue(draw.Component.RenderId, out var emptied))
                {
                    foreach (var unit in emptied.Units) Retire(unit);
                    emptied.ClearUnits();
                }
                continue;
            }

            _groupById.TryGetValue(draw.Component.RenderId, out var existing);
            // (Re)place in the paint order? Brand new, coming back from hidden, or MOVED (a recycled container re-added
            // elsewhere). The InOrder check matters: a container shown again at the SAME rank still needs re-inserting - a
            // rank compare alone would silently leave it out of the order, drawn nowhere.
            var replace = existing == null || !existing.InOrder || existing.Order != draw.Order;

            var group = BuildUnitsFor(draw.Component, draw.Commands, packet.ProjectionMatrix);
            group.Clones = draw.Clones;   // the THIRD apply path - a clone set has to arrive on all of them, not two
            group.Order = draw.Order;

            if (replace) QueueInsert(group);
        }

        // 4. Kept its units, but its place changed (a recycled container, one shown again, or everything at once after a
        //    renumber): nothing to re-record - just put its group back where the tree now says it belongs.
        foreach (var (component, order) in packet.Reranks)
        {
            if (!_groupById.TryGetValue(component.RenderId, out var group)) continue;
            group.Order = order;
            QueueInsert(group);
        }

        FlushOrderRemovals();   // the merge below READS the order, so the batch has to be committed first

        if (_pendingInserts.Count == 0) return;

        // One merge of two sorted sequences - the retained paint order and this frame's arrivals.
        _pendingInserts.Sort(static (a, b) => a.Order.CompareTo(b.Order));
        _mergedGroups.Clear();
        var i2 = 0;
        var j2 = 0;
        while (i2 < _groups.Count && j2 < _pendingInserts.Count)
            _mergedGroups.Add(_groups[i2].Order <= _pendingInserts[j2].Order ? _groups[i2++] : _pendingInserts[j2++]);
        while (i2 < _groups.Count) _mergedGroups.Add(_groups[i2++]);
        while (j2 < _pendingInserts.Count) _mergedGroups.Add(_pendingInserts[j2++]);

        _groups.Clear();
        _groups.AddRange(_mergedGroups);
        foreach (var group in _pendingInserts)
        {
            group.InOrder = true;

            LayerProbe.Say($"back in the order: {Named(group)} unrecorded={group.Unrecorded} runs={group.Runs.Count}"
                           + $" units={group.Units.Count}");

            if (!group.Unrecorded) continue;

            // It is BACK, and what it drew is not. While it was out of the order its instances were blanked and its
            // arena slots handed back (BlankOrphanInstances), so the units that survived describe bytes that no longer
            // exist - re-inserting it puts an empty group into the paint order, holding its slot and drawing nothing.
            // Told to the recorder, which owns the mirror that still claims those units (see NoteUnrecorded).
            group.Unrecorded = false;
            NoteUnrecorded(group.Component);
        }

        // A group JOINED the paint order, so the recorded stream has no ops for it at all and a patch cannot show what
        // was not there. Departures are answered by _leftTheOrder; this is the arrival.
        _orderJoined = true;
    }

    // Groups leaving the paint order; removals are batched into one pass, since a whole view can leave at once.
    private readonly HashSet<ControlGroup> _orderBatch = new();
    private bool _batchOrderRemovals;

    private void RemoveFromOrder(ControlGroup group, string why = null)
    {
        if (!group.InOrder) return;
        group.InOrder = false;

        if (why != null) LayerProbe.Say($"out of the order ({why}): {Named(group)} tag={group.Tag} units={group.Units.Count}");

        if (_batchOrderRemovals) _orderBatch.Add(group);
        else _groups.Remove(group);
        _leftTheOrder.Add(group);   // its instances are still in the arena - see BlankOrphanInstances
    }

    /// <summary>Commit a batch of removals in ONE pass and go back to removing singly. Must run before anything reads
    /// the paint order again - the inserts below do, which is why the batch spans only the two departure loops.</summary>
    private void FlushOrderRemovals()
    {
        if (_orderBatch.Count > 0) _groups.RemoveAll(_orderBatch.Contains);
        _orderBatch.Clear();
        _batchOrderRemovals = false;
    }

    // Queue a group for this frame's ONE merge into the paint order. Deduped: the same group can be named twice in a packet
    // (a renumber reranks everything, and a re-recorded component carries its rank on its draw) - inserting twice would
    // draw it twice.
    private void QueueInsert(ControlGroup group)
    {
        RemoveFromOrder(group, "requeue");   // no-op when it is not in the order (new, or hidden)
        if (_pendingSet.Add(group)) _pendingInserts.Add(group);
    }

}

