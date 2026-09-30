using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.Graphics.Core;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media.Animation;
using Adamantium.UI.Rendering.Retained;
using Adamantium.UI.Rendering.RenderUnits;
using Adamantium.Vulkan.Core;

namespace Adamantium.UI.Rendering;

// What one recorded draw IS. At namespace scope because an ARENA is asked whether a recorded op draws it (see
// BatchArena.MatchesOp) - the instanced fill is drawn by a flush op, not by a segment op, and that is its own business.
internal enum RenderOpKind : byte { Scissor, Unit, Segment, InstancedFlush }

public partial class RenderCache
{
    // Item-background batch (solid rounded-rect fills -> one SDF-AA'd instanced draw). Rects are the LOWER layer
    // (FlushBatches draws rects THEN text). Both batches share one clip GROUP (_batchScissor): a scissor (or text-atlas)
    // change flushes both together, preserving order.
    private RectBatchCollector _rectBatch;
    private EllipseBatchCollector _ellipseBatch;   // SDF family, same fill layer as rects (below text)
    private RegularPolygonCollector _polygonBatch; // ...and its polygon sibling, in that same fill layer

    // GPU-resident transform table (one world matrix per MOTION NODE; slot 0 = identity for legacy world-space bakes). The
    // SDF vertex shaders fetch each instance's matrix by slot, so moving a node costs ONE matrix write instead of re-baking
    // its instances - and rotated/3D instances stay batched. Owned per cache; initialized in the Render device block.
    private TransformTable _transformTable;

    // Set only while a clone run is being drawn; null on every ordinary group, which is what keeps the hot loop
    // paying one null check instead of a matrix multiply.
    private Matrix4x4F? _cloneMatrix;

    /// <summary>Index just past the prototype's subtree in <see cref="_groups"/>. Paint rank is DFS order, so a subtree
    /// is a CONTIGUOUS run: scan forward while each group's control is a visual descendant of the prototype.</summary>
    private int CloneSubtreeEnd(int start)
    {
        var prototype = _groups[start].Component;
        var end = start + 1;
        while (end < _groups.Count && IsVisualDescendantOf(_groups[end].Component, prototype)) end++;
        return end;
    }

    private static bool IsVisualDescendantOf(IUIComponent node, IUIComponent ancestor)
    {
        for (var p = node?.VisualParent; p != null; p = p.VisualParent)
        {
            if (ReferenceEquals(p, ancestor)) return true;
        }

        return false;
    }
    private GradientRectCollector _gradientRectBatch;   // SDF family: rounded rects with a linear/radial GRADIENT fill
    private GradientEllipseCollector _gradientEllipseBatch;   // SDF family: ellipses with a linear/radial GRADIENT fill
    private PatternRectCollector _patternBatch;   // SDF family: rounded rects with a PROCEDURAL pattern fill (checker/stripes/dots/grid)

    // SDF family: shapes whose fill is a BACKDROP MATERIAL - acrylic, mica, liquid glass. Created on the first one, and
    // flushed LAST of the fills, because it reads the frame that the others have already drawn.
    private MaterialRectCollector _materialBatch;
    private readonly Dictionary<int, (IUIComponent Node, Matrix4x4F World)> _matSegNode = new();
    private FractalRectCollector _fractalBatch;   // SDF family: rounded rects with an escape-time FRACTAL fill (Julia/Mandelbrot)
    private CanvasGridCollector _canvasGridBatch;   // the canvas GROUND: one quad, the fragment deciding if it is on a mark
    private InkCollector _inkBatch;   // INK: a capsule per segment of a stroke, all of them one instanced draw
    private CanvasArrowCollector _arrowBatch;   // ARROWS and plain lines: one quad each, shaft and heads decided per pixel
    private TextureBatchCollector _texRectBatch;   // SDF family: rounded rects whose fill is SAMPLED from a texture (ImageBrush / NineSliceBrush)
    // The soft band (aura / shadow) in its TWO paint positions. An OUTER band goes under every fill; an INNER one over
    // them - drawn under, it would simply be covered by the shape's own fill. Both lazy: most windows have neither.
    private HaloRectCollector _haloUnder;
    private HaloRectCollector _haloOver;
    // The LIVING aura rides its own pass, so it has its own pair - flushed right beside their still twins.
    private HaloLivingCollector _haloLivingUnder;
    private HaloLivingCollector _haloLivingOver;
    // Whose inner band is pending. Its OWN fill must not flush it - the fill is added right after the band and the two
    // belong together; anyone ELSE overlapping it still forces a flush, or a later sibling would be painted over.
    private IUIComponent _haloOverOwner;
    private Rect2D _batchScissor;
    // WHOSE clip the pending batch sits under - the VIEWPORT (ClipOwnerOf), not the last element added. A flush cycle
    // ends when the rect changes OR when that owner does, so everything in a segment really does share one clip: the
    // replay re-derives the segment's frozen rect from this one owner, and members of any other are cut away.
    private IUIComponent _batchClip;
    private bool _batchOpen;

    /// <summary>Has the open batch's CLIP GROUP ended? Both halves matter: the rect, because a segment carries one
    /// scissor, and the OWNER, because the replay re-derives that scissor from it alone.</summary>
    private bool ClipGroupChanged(Rect2D scissor, IUIComponent c)
        => _batchOpen && (!ScissorEquals(_batchScissor, scissor) || !ReferenceEquals(_batchClip, ClipOwnerOf(c)));

    // General instanced fills (arbitrary tessellated geometry sharing a mesh), flushed in PAINT ORDER via FlushBatches.
    // Own buffer manager, distinct from the per-unit geometry buffers.
    private GpuBufferManager _instanceBuffers;
    private InstancedFillCollector _instancedFill;

    // --- Retained draw (clean-frame op replay) ---
    // Non-clean frames record the ordered GPU op stream; clean frames replay it over the retained buffers instead of
    // re-baking. _opsReplayable is the escape hatch for draws the stream cannot reproduce.
    private struct RenderOp
    {
        public RenderOpKind Kind;
        public Rect2D Scissor;    // Scissor
        // Scissor: WHOSE clip this rect is - the component the rect was derived from (CumulativeClip). A scissor is the
        // one thing in the stream that is a WORLD-SPACE rect, so it is also the one thing a move stales; naming its owner
        // is what lets the rect be derived again instead of the whole frame being re-recorded. Null on the op that
        // restores the full window scissor - that rect belongs to the window and no move touches it.
        public IUIComponent Clip;
        public IRenderUnit Unit;  // Unit
        public byte Batch;        // Segment: which collector (0 rect, 1 ellipse, 2 text, 3 gradient-rect, 4 gradient-ellipse, 5 pattern, 6 fractal, 7 textured)
        // Segment: the collector's STABLE segment id (see BatchCollector.Segment.Id) - never an index, so a split that
        // inserts a segment in the middle of the draw order leaves every recorded op naming exactly what it named before.
        // InstancedFlush: that collector's flush index (its list is append-only within a frame, so there is nothing to shift).
        public int SegId;

        // Paint rank of the group being recorded when this op was emitted. The stream is written in rank order, so a
        // control that starts drawing later knows exactly where its op belongs - by its OWN rank, not by guessing from
        // whatever happens to sit next to it. Guessing is what made an unrelated diagnostics label in the corner decide
        // whether hovering a tile cost 0.9 ms or a full walk.
        public long Order;

        // A segment spans ranks [OrderFirst, Order]; a newcomer ranked strictly inside cannot be placed around it. Equals
        // Order for other ops.
        public long OrderFirst;
    }
    private readonly List<RenderOp> _ops = new();

    // The same stream, said in the terms the frame is actually built in: one entry per LAYER (see RenderLayer), in draw
    // order, each owning a range of _ops and the rank INTERVAL it covers. The ops stay one contiguous array - that is what
    // a replay walks - and the layers are what gives that array its structure: where a newcomer belongs, and which set it
    // may join without its order mattering.
    private readonly List<RenderLayer> _layers = new();

    private long _recordOrder;   // paint rank of the group currently being recorded - stamped onto every op it emits

    // The layer being written. A layer runs until the batches are FLUSHED, because that is exactly when the engine itself
    // decides "what came before cannot be reordered with what comes next" - the flush happens on an overlap.
    private RenderLayer _openLayer;

    /// <summary>Appends an op to the stream and to the layer it belongs to. Every recorded op goes through here, so the
    /// layer list can never disagree with the stream about what the frame draws.</summary>
    private void RecordOp(in RenderOp op)
    {
        if (_openLayer == null)
        {
            _openLayer = new RenderLayer { OpFirst = _ops.Count };
            _layers.Add(_openLayer);
        }

        _ops.Add(op);
        _openLayer.OpCount++;
        _openLayer.Cover(op.Order);
        if (op.Kind == RenderOpKind.Segment)
        {
            _openLayer.Cover(op.OrderFirst);
            _openLayer.Runs.Add((op.Batch, op.SegId));
        }
    }

    /// <summary>Ends the layer being written: whatever is recorded next is strictly after everything in it.</summary>
    private void CloseLayer() => _openLayer = null;

    // Group identity as the ARENA sees it: written into every instance the group bakes, so a slot can always name its
    // owner however many times its bytes have been copied around since (see RectBatchCollector.TryAdd).
    private readonly Dictionary<int, ControlGroup> _groupByTag = new();
    private int _nextGroupTag;

    private int TagOf(ControlGroup group)
    {
        if (group.Tag == 0)
        {
            group.Tag = ++_nextGroupTag;
            _groupByTag[group.Tag] = group;
        }

        return group.Tag;
    }

    // Groups that left the paint order since the last sweep, listed so the arena is not scanned on every hiding frame.
    private readonly List<ControlGroup> _leftTheOrder = new();
    private bool _orderJoined;   // a group arrived since the recorded walk - see ApplyStructural / TryPartialReplay

    // The departed groups' tags, as one set, so the arena is asked once instead of once per group.
    private readonly HashSet<int> _departedTags = new();

    private readonly HashSet<int> _emptiedSegments = new();

    /// <summary>Takes a rect segment's op out of the recorded stream - the segment draws nothing any more - and tells the
    /// layer that held it. The layer keeps its place in the frame: what it lost is one run, not its interval.</summary>
    private void DropSegmentOp(int segId)
    {
        for (var i = 0; i < _ops.Count; i++)
        {
            if (_ops[i].Kind != RenderOpKind.Segment || _ops[i].Batch != 0 || _ops[i].SegId != segId) continue;

            _ops.RemoveAt(i);
            NoteOpRemoved(i);
            return;
        }
    }

    /// <summary>The mirror of <see cref="NoteOpInserted"/>: the layer that held the op loses one, and everything behind
    /// it moves back. The layers must keep tiling the stream exactly - a range that has slid by one is a frame assembled
    /// out of its neighbors' pieces.</summary>
    private void NoteOpRemoved(int index)
    {
        var taken = false;
        for (var i = 0; i < _layers.Count; i++)
        {
            var layer = _layers[i];
            if (taken)
            {
                layer.OpFirst--;
                continue;
            }

            if (index < layer.OpFirst)
            {
                layer.OpFirst--;
                continue;
            }

            if (index < layer.OpFirst + layer.OpCount)
            {
                layer.OpCount--;
                taken = true;
            }
        }

        foreach (var layer in _layers)
        {
            for (var r = layer.Runs.Count - 1; r >= 0; r--)
                if (layer.Runs[r].Batch == 0 && !_rectBatch.HasSegment(layer.Runs[r].SegId)) layer.Runs.RemoveAt(r);
        }
    }

    // Blanks instances of departed controls that live segments still issue as part of their range; the next recording
    // walk reclaims the slots.
    private void BlankOrphanInstances(IGraphicsDevice device)
    {
        if (_rectBatch == null || device == null) return;

        _emptiedSegments.Clear();

        // Blank by owner tag first (recorded runs may name other groups' slots by now); the run walk then hands slots
        // back at segment edges.
        _departedTags.Clear();
        foreach (var group in _leftTheOrder)
        {
            if (group.InOrder || group.Tag == 0) continue;
            // No arena check: the scan blanks only instances that CARRY this tag, and a tag belongs to one group by
            // construction - so it can never reach anyone else's slots. A disposed group has already dropped its arena
            // reference, and requiring one here is what left its instances painting.
            _departedTags.Add(group.Tag);
        }

        _rectBatch.BlankOwnedAnywhere(device, _departedTags);

        foreach (var group in _leftTheOrder)
        {
            if (group.InOrder || group.Tag == 0) continue;   // came back before anyone looked

            LayerProbe.Say($"left the order: {Named(group)} arena={ArenaName(group)} tag={group.Tag} runs={group.Runs.Count}"
                           + $" units={group.Units.Count}");

            // OWNERSHIP rides in the instance, and only a rect instance carries it (RectItem.OwnerTag). Another family's
            // orphans are left to the next walk rather than blanked on a guess about whose slots those are.
            if (!ReferenceEquals(group.Arena, _rectBatch))
            {
                LayerProbe.Say($"  ...not swept: {Named(group)} draws in {ArenaName(group)}, so nothing marks it unrecorded");
                continue;
            }
            var reclaimedAll = true;
            foreach (var run in group.Runs)
            {
                var segment = _rectBatch.FindSegmentContaining(run.First);
                _rectBatch.BlankOwned(device, (uint)run.First, (uint)run.Count, group.Tag);

                // ...and give the run back where it can be given back at all: at an edge of its segment the range simply
                // shrinks, so those instances stop being ISSUED rather than being issued blank. In the middle they stay
                // blanked - see BatchCollector.ReclaimRun for why splitting to reclaim them is the wrong trade.
                reclaimedAll &= _rectBatch.ReclaimRun(run.First, run.Count);
                if (segment >= 0) _emptiedSegments.Add(segment);
            }

            // Runs it no longer owns anywhere: keeping them would let a later patch address space that is now free.
            if (reclaimedAll) group.Runs.Clear();

            // Its bytes are gone though its units survive, so the recorder must re-record it on return (NoteUnrecorded).
            group.Unrecorded = true;
        }

        // A segment whose every instance has just been blanked is a draw call that paints nothing. Let it go: the op
        // leaves the stream and the layer holding it loses that run - which is what "an empty layer closes" means in
        // practice. The slots stay allocated until the next recording walk re-lays the arena, because they still sit
        // inside a range other segments' draws are indexed against.
        foreach (var id in _emptiedSegments)
        {
            if (!_rectBatch.SegmentDrawsNothing(id)) continue;
            DropSegmentOp(id);
        }
    }

    // For the watch above only - a group is named by the control it draws for, which is the only name a person can
    // match against what they just did.
    private static string Named(ControlGroup group) =>
        group.Component is not { } component
            ? "(no control)"
            : component.GetType().Name
              + (string.IsNullOrEmpty(component.Name) ? "" : $" '{component.Name}'")
              // WHOSE CHILD IT IS, because a type name alone names half the panel: the picture on the plane and the
              // icon on a button are both an Image, and only the parent says which one just went dark.
              + $" in {component.RenderParent?.GetType().Name ?? "NOWHERE"}";

    private string ArenaName(ControlGroup group)
    {
        if (group.Arena == null) return "none";
        if (ReferenceEquals(group.Arena, _rectBatch)) return "rect";
        if (ReferenceEquals(group.Arena, _textBatch)) return "text";

        return group.Arena.GetType().Name;
    }

    /// <summary>How many draw operations the recorded frame replays. Tests read it to see that work actually LEFT the
    /// frame - a control that stopped drawing should cost one draw call less, not one blanked instance more.</summary>
    public int RecordedOpCount => _ops.Count;

    /// <summary>Whether the layers still describe the stream they were built from: their op ranges tile it in order,
    /// leaving no op out and claiming none twice. Asked by tests, because a layer list that has drifted would place a
    /// newcomer into a set it does not belong to - a wrong picture, not a slow frame.</summary>
    public bool LayersDescribeTheStream(out string why)
    {
        var at = 0;
        foreach (var layer in _layers)
        {
            if (layer.OpFirst != at)
            {
                why = $"layer {layer} does not begin where the previous one ended ({at})";
                return false;
            }

            at += layer.OpCount;
        }

        why = at == _ops.Count ? null : $"layers cover {at} ops, the stream holds {_ops.Count}";
        return at == _ops.Count;
    }

    /// <summary>The layer an op index now belongs to, after an insert moved everything behind it along.</summary>
    private void NoteOpInserted(int index)
    {
        // Every insert must grow exactly one layer, or later layers' windows slide; an op on a boundary belongs to the
        // layer ending there.
        var taken = -1;
        for (var i = 0; i < _layers.Count; i++)
        {
            var layer = _layers[i];
            var end = layer.OpFirst + layer.OpCount;

            if (index >= layer.OpFirst && index < end) { taken = i; break; }
            if (index == end) { taken = i; continue; }   // the boundary: this layer takes it, unless the next one starts here
            if (index < layer.OpFirst) { if (taken < 0) taken = i; break; }
        }

        if (taken < 0) taken = _layers.Count - 1;
        if (taken < 0) return;   // nothing recorded yet - the op opens the stream, and the walk will open its layer

        _layers[taken].OpCount++;
        for (var i = taken + 1; i < _layers.Count; i++) _layers[i].OpFirst++;
    }

    // Where the rect batch's PENDING segment's paint span begins: the rank of the first group that put something in it.
    // Noted while the segment is still empty, because afterwards there is nothing left to read it from - and a splice that
    // places a newcomer needs the span's START, not just where it ended (see PlaceNewSegment).
    private long _rectSegStart;

    // The transform-table version the stream was recorded against: scissors and per-unit worlds are baked, while batch
    // segments follow slots live, so a moved matrix makes them disagree on replay.
    private ulong _opsMatrixVersion;

    // Set by the applier when a packet folds a new layout snapshot in; cleared when a walk re-records the stream against
    // that layout. While it is set, the retained stream describes an older frame than the snapshot does.
    private bool _layoutChangedSinceRecord;

    private ulong _opsLayoutVersion;

    private bool OpsMatchTransforms
    {
        get
        {
            if (_layoutChangedSinceRecord) return false;
            if (_transformTable == null) return true;
            if (_transformTable.MatrixVersion == _opsMatrixVersion) return true;   // nothing moved at all

            // Only layout moves are baked into the ops; compositor moves are re-pointed at replay (ExecuteOps).
            if (_transformTable.LayoutMatrixVersion != _opsLayoutVersion) return false;

            return CompositedMovesKeepOpsValid();
        }
    }

    // The one thing a composited move CAN invalidate: a recorded Scissor op, which is a world-space rect. It goes stale
    // when the mover clips (its own rect moved with it) or when something inside it clips (that rect is positioned by the
    // mover). A spinner, a pulse, a slid-in panel with no clip inside it - none of those touch a clip, which is the common
    // case this exists for.
    private bool CompositedMovesKeepOpsValid()
    {
        foreach (var owner in _compositedOwners)
        {
            if (SubtreeClips(owner)) return false;
        }

        return true;
    }

    private static bool SubtreeClips(IUIComponent node)
    {
        if (node == null) return false;
        if (node.ClipToBounds) return true;

        foreach (var child in node.VisualChildren)
        {
            if (SubtreeClips(child)) return true;
        }

        return false;
    }
    private bool _recording;       // this frame runs the walk and is appending ops
    private bool _opsRecorded;     // _ops holds a complete frame from a prior walk
    private bool _opsReplayable;   // the recorded stream faithfully reproduces the frame (currently always true - see above)

    private readonly List<Compositor.Entry> _compositedBuf = new();   // this thread's view of the composited set (reused)
    private readonly Dictionary<IUIComponent, (LayoutSnapshot Snap, Matrix4x4F ParentWorld)> _compositedFallback = new();   // keep animating across a settling swap's snapshot re-capture
    private readonly HashSet<IUIComponent> _compositedOwners = new();   // motion nodes the compositor moved THIS present (ExecuteOps re-Updates their per-unit draws)

    private const int MaxRetainedOps = 256;   // op stream past this -> a splice yields to a full walk that recompacts
    private readonly List<GroupPatch> _patchBuf = new();   // TrySplicedPatch: staged per-group patches (validated before mutation)
    private readonly HashSet<int> _patchLayers = new();  // the LAYERS those patches land in - each re-issued once, whole
    private int _cloneReserve;   // clone slots this frame reserved - counted once, read by the trace
    private readonly List<RectItem> _rebakeBuf = new();

    // Picks this frame's transform-table copy and gives its address to every collector; called again after the walk
    // creates collectors, since replays skip the walk's setup.
    private void BeginTransformFrame(IGraphicsDevice device)
    {
        if (device == null) return;

        if (_transformTable == null)
        {
            _transformTable = new TransformTable();
            _transformTable.EnsureResources(device);
            _transformTable.SetMatrix(device, _transformTable.AcquireSlot(Guid.Empty), Matrix4x4F.Identity);
        }

        // Every clone takes a slot of its own, and a clone run asks for its whole set in ONE frame. The buffer is sized
        // here and nowhere else, so the count has to be known BEFORE it is made - discovering it while baking leaves the
        // overflow unuploaded and the shader reading past the buffer (tiles vanishing and jumping, differently each
        // frame). Cheap: this scans groups, and only a clone host contributes.
        var reserve = 0;
        foreach (var group in _groups)
        {
            if (group.Clones is { Count: > 0 } clones) reserve += clones.Count;
        }

        _cloneReserve = reserve;   // ...and the trace reads it from here instead of counting the scene again
        _transformTable.Reserve(reserve);
        _transformTable.EnsureResources(device);

        var address = _transformTable.DeviceAddress;
        if (_rectBatch != null) _rectBatch.TransformsAddress = address;
        if (_ellipseBatch != null) _ellipseBatch.TransformsAddress = address;
        if (_polygonBatch != null) _polygonBatch.TransformsAddress = address;
        if (_gradientRectBatch != null) _gradientRectBatch.TransformsAddress = address;
        if (_gradientEllipseBatch != null) _gradientEllipseBatch.TransformsAddress = address;
        if (_patternBatch != null) _patternBatch.TransformsAddress = address;
        if (_fractalBatch != null) _fractalBatch.TransformsAddress = address;
        if (_canvasGridBatch != null) _canvasGridBatch.TransformsAddress = address;
        if (_inkBatch != null) _inkBatch.TransformsAddress = address;
        if (_arrowBatch != null) _arrowBatch.TransformsAddress = address;
        if (_texRectBatch != null) _texRectBatch.TransformsAddress = address;
        if (_materialBatch != null)
        {
            _materialBatch.TransformsAddress = address;

            _materialBatch.WindowBoundsProvider = WindowOnDesktop;

            // The window origin, latched once per frame (replays included) so mica follows drags and all panes share one
            // instant.
            _materialBatch.LatchWindow();
        }
        if (_haloUnder != null) _haloUnder.TransformsAddress = address;
        if (_haloOver != null) _haloOver.TransformsAddress = address;
        if (_haloLivingUnder != null) _haloLivingUnder.TransformsAddress = address;
        if (_haloLivingOver != null) _haloLivingOver.TransformsAddress = address;
        if (_textBatch != null) _textBatch.TransformsAddress = address;   // glyph VS fetches the node matrix AND the clip by slot
        if (_instancedFill != null) _instancedFill.TransformsAddress = address;
    }

    /// <summary>The out-of-render-pass sweep (shared-surface latch copies), run once per frame before BeginRendering over
    /// a short list of units that need it (see RenderUnit.MachineryVersion).</summary>
    public void PreRender()
    {
        var machinery = RenderUnits.RenderUnitMachinery.Version;
        var membership = ControlGroup.MembershipVersion;
        if (_preRenderWalkVersion != membership || _preRenderMachineryVersion != machinery)
        {
            _preRenderUnits.Clear();
            foreach (var group in _groups)
            foreach (var unit in group.Units)
            {
                if (unit.NeedsPreRender) _preRenderUnits.Add(unit);
            }

            _preRenderWalkVersion = membership;
            _preRenderMachineryVersion = machinery;
        }

        foreach (var unit in _preRenderUnits) unit.PreRender();
    }

    /// <summary>Renders every cached unit with no scissor management. Used by GPU-free tests (no device).</summary>
    public void Render() => Render(null, default);

    /// <summary>Renders every cached unit, narrowing the Vulkan scissor per unit to the intersection of its
    /// <see cref="IUIComponent.ClipToBounds"/> ancestors' bounds. <paramref name="fullScissor"/> is restored for unclipped
    /// units.</summary>
    public void Render(IGraphicsDevice device, Rect2D fullScissor)
    {
        // TEMP trace: one array write per frame, dumped from memory by the overlay.
        var traceStart = Core.Diagnostics.FrameTrace.Enabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
        LastFrameReplayed = false;
        DrawWalkMs = 0;
        _traceWhy = 0;
        try
        {
            RenderCore(device, fullScissor);
        }
        finally
        {
            DrawReplayed = LastFrameReplayed;

            // Swept here, after the frame is finished, and on replayed frames too, which RenderCore returns early from.
            if (_leftTheOrder.Count > 0 && device != null)
            {
                BlankOrphanInstances(device);
                _leftTheOrder.Clear();
            }

            // Cleared where the departures are: both say "the paint order is not the one the stream was recorded
            // against", and both are answered by the frame that has just been drawn.
            _orderJoined = false;

            if (Core.Diagnostics.FrameTrace.Enabled)
            {
                // Counted where they are DECIDED, not by scanning the scene here: this runs on every frame the plate is
                // up - which is every frame - and a diagnostic that walks the whole paint order to report a number is the
                // instrument becoming the thing it measures.
                var clones = _cloneReserve;

                var unitOps = 0;
                foreach (var op in _ops)
                {
                    if (op.Kind == RenderOpKind.Unit) unitOps++;
                }

                Core.Diagnostics.FrameTrace.Add(
                    System.Diagnostics.Stopwatch.GetElapsedTime(traceStart).TotalMilliseconds,
                    (byte)LastBuildKind, LastFrameReplayed, clones, _traceWhy, _traceCacheId, _traceComposited,
                    _ops.Count, unitOps);
            }
        }
    }

    /// <summary>This cache's last draw phases, in RenderCore order: Setup, Paint, Moved, Ops, Walk. Per cache, since a
    /// frame draws through several caches.</summary>
    public double DrawSetupMs { get; private set; }
    public double DrawPaintMs { get; private set; }
    public double DrawMovedMs { get; private set; }
    public double DrawOpsMs { get; private set; }
    public double DrawWalkMs { get; private set; }

    /// <summary>The three calls Paint is made of, so the phase names a CALL and not a group of them: the composited
    /// animations this thread plays by itself, the arena repaint, and the brush repaints. All three run before the
    /// frame decides between replaying and walking, so all three are paid on a scene where nothing has changed.</summary>
    public double DrawAnimMs { get; private set; }
    public double DrawArenaPaintMs { get; private set; }
    public double DrawBrushRepaintMs { get; private set; }

    /// <summary>Whether this cache's last draw replayed the recorded stream instead of walking - the bit that says which
    /// of the numbers above describe it.</summary>
    public bool DrawReplayed { get; private set; }

    /// <summary>Did the last frame REPLAY the recorded op stream (patching only what changed) instead of walking the tree?
    /// A walk is O(scene) and a replay is O(dirty). Tests assert it, so a regression to "one dirty element re-draws
    /// everything" is caught as a failure rather than as a slower frame nobody notices.</summary>
    public bool LastFrameReplayed { get; private set; }

    /// <summary>Why the last frame WALKED instead of patching: 0 none, 1 nothing recorded, 2 stream unusable, 3 transform
    /// dirty, 4 layout changed since the record, 5 the splice refused, 6 the slot patch refused. A walk is O(scene) where a
    /// patch is O(changed), so every non-zero answer here is a frame that cost the whole scene - and the reason has to be
    /// askable from a test, not only from a live trace.</summary>
    public byte LastWalkReason => _traceWhy;

    private byte _traceWhy;

    // TEMP trace: several caches (one per window, plus each window's adorner layer) write into one ring, so a frame has to
    // say whose it is - otherwise a quiet cache's cheap frames and a busy one's expensive frames read as one bimodal blur.
    /// SCRATCH: the window cache, so a probe can ask what the recorded stream actually draws while a phantom is on screen.
    public static RenderCache LastCache;

    /// SCRATCH: what this cache would replay right now - one line per group that owns retained rect slots.
    public string DumpGroups()
    {
        var sb = new System.Text.StringBuilder($"ops {_ops.Count} (replayed={LastFrameReplayed}, kind={LastBuildKind}), groups {_groups.Count}");
        sb.Append(System.Environment.NewLine);
        foreach (var g in _groups)
        {
            var slots = 0;
            foreach (var r in g.Runs) slots += r.Count;
            if (slots == 0 && g.Units.Count == 0) continue;

            var c = g.Component;
            sb.Append($"  {c?.GetType().Name} vis={c?.Visibility} op={ApplySnap(c).SelfOpacity:0.00} bounds={c?.Bounds} slots={slots} units={g.Units.Count} walk={(g.WalkVersion == _walkVersion ? "current" : "STALE")}")
              .Append(System.Environment.NewLine);
        }

        return sb.ToString();
    }

    private static int _traceNextCacheId;
    private readonly int _traceCacheId = System.Threading.Interlocked.Increment(ref _traceNextCacheId);
    private int _traceComposited;

    // Diagnostics: ADAM_NO_PATCH=1 disables patching, ADAM_NO_REPLAY=1 disables replay. Settable so tests can force the
    // walk.
    internal static bool PatchDisabled = Environment.GetEnvironmentVariable("ADAM_NO_PATCH") == "1";
    internal static bool ReplayDisabled = Environment.GetEnvironmentVariable("ADAM_NO_REPLAY") == "1";

    private void RenderCore(IGraphicsDevice device, Rect2D fullScissor)
    {
        // This frame's transform-table copy, picked BEFORE anything writes a matrix or draws - the composited animations
        // below write matrices, and the replay paths below draw without ever reaching the walk's setup block.
        var phase0 = System.Diagnostics.Stopwatch.GetTimestamp();
        BeginTransformFrame(device);

        // Both memos are WITHIN-frame and must drop together: a clip recomputed against a stale world is worse than a
        // stale clip. Only a full rebuild dropped them otherwise, so a viewport that travels without announcing a move -
        // one nested in a scroller, or a RECYCLED container now standing somewhere else - cut its own content with last
        // frame's rect. Measured on a tab strip in an open details panel: 28 bad frames of 40, then 0.
        _worldCache.Clear();
        _relWorldCache.Clear();
        _clipCache.Clear();
        // NOT _clipOwnerCache: which viewport cuts an element is STRUCTURAL - the ClipToBounds flags and the render
        // parent chain - so it survives every frame in which the tree did not change, and the applier drops it when a
        // packet changes the snapshot it is derived from. Dropping it here instead cost a recursive walk up the chain
        // per unit per frame: measured on the grid tab at rest, 453 -> 336 fps and draw 1,6 -> 2,2 ms.

        // Rounded clips, refreshed from their owners BEFORE the clean-frame early-out - for the same reason the
        // composited animations below run there: a replayed frame re-records nothing, so a clip that changed shape
        // reaches the screen only through its slot.
        _frameScissor = fullScissor;   // the frame's own, for anything that has to ask RoundedClipSlot outside the walk
        RefreshClipSlots(fullScissor);
        DrawSetupMs = System.Diagnostics.Stopwatch.GetElapsedTime(phase0).TotalMilliseconds;
        phase0 = System.Diagnostics.Stopwatch.GetTimestamp();

        // The animations this thread plays by itself. BEFORE the clean-frame early-out on purpose: a composited animation
        // changes what the retained op stream draws (a matrix, a re-baked color slot), so an otherwise CLEAN frame is
        // exactly when it must still apply - the loop can be stalled in a theme cascade and the spinner keeps turning.
        if (_transformTable != null) _transformTable.CompositedWrite = true;
        ApplyCompositedAnimations(device);
        if (_transformTable != null) _transformTable.CompositedWrite = false;
        DrawAnimMs = System.Diagnostics.Stopwatch.GetElapsedTime(phase0).TotalMilliseconds;
        var paint0 = System.Diagnostics.Stopwatch.GetTimestamp();

        // Recolors reach the arena before choosing replay, patch or walk, so text recolors on replayed frames too.
        ApplyPaintToArenas(device);
        DrawArenaPaintMs = System.Diagnostics.Stopwatch.GetElapsedTime(paint0).TotalMilliseconds;
        paint0 = System.Diagnostics.Stopwatch.GetTimestamp();
        ApplyBrushRepaints(device);
        DrawBrushRepaintMs = System.Diagnostics.Stopwatch.GetElapsedTime(paint0).TotalMilliseconds;
        DrawPaintMs = System.Diagnostics.Stopwatch.GetElapsedTime(phase0).TotalMilliseconds;
        phase0 = System.Diagnostics.Stopwatch.GetTimestamp();

        // Clean-frame replay: re-issue the last recorded walk's op stream and skip the per-unit loop (the retained buffers
        // still hold its bytes). Only a fully-Clean build qualifies; a Partial/Full re-walks and re-records.
        if (device != null && !ReplayDisabled && _opsRecorded && _opsReplayable && LastBuildKind == RenderBuildKind.Clean && OpsMatchTransforms
            && RefreshMovedNodes(device)          // moved nodes first: write their matrices, or the replay draws them where they were
            && RefreshMovedComponents(device, fullScissor))    // ...and the ordinary movers' subtrees, for the same reason
        {
            RefreshMovedScissors(fullScissor);    // the viewports they carried past are world-space rects - derive again
            AcceptPatchedTransforms();
            LastFrameReplayed = true;
            DrawMovedMs = System.Diagnostics.Stopwatch.GetElapsedTime(phase0).TotalMilliseconds;
            phase0 = System.Diagnostics.Stopwatch.GetTimestamp();
            ExecuteOps(device, fullScissor);
            DrawOpsMs = System.Diagnostics.Stopwatch.GetElapsedTime(phase0).TotalMilliseconds;
            return;
        }

        // Fast-path PARTIAL replay: a geometry-only partial that only recolored/updated already-batched tiles in place (no
        // splice). Patch just those slots, then replay - O(dirty). ONLY when nothing MOVED (!LastBuildTransformDirty):
        // ExecuteOps redraws batch segments from last frame's baked positions, so a MOVE would leave batched fills stale
        // while per-unit draws follow the new transform (the "outline runs ahead of its fill" tear) -> fall through to the walk.
        if (device != null && !PatchDisabled && _opsRecorded && _opsReplayable && LastBuildKind == RenderBuildKind.Partial
            && !LastBuildTransformDirty && OpsMatchTransforms && !_partialSpliced && _rectBatch != null && TryPartialReplay(device, fullScissor))
        { LastFrameReplayed = true; return; }

        // SPLICED partial patch: a dirty control's unit COUNT changed (hover background 0<->1, a live chart re-recording a
        // different number of segments). Its group re-rendered in place; here the retained BATCH is patched by segment
        // surgery (excise its old run, its re-baked items append as a new segment spliced into the op stream at the same
        // paint position) then replayed. O(dirty groups). Falls back to the full walk on anything not yet patchable.
        if (device != null && !PatchDisabled && _opsRecorded && _opsReplayable && LastBuildKind == RenderBuildKind.Partial
            && !LastBuildTransformDirty && OpsMatchTransforms && _partialSpliced && _rectBatch != null && TrySplicedPatch(device, fullScissor))
        { LastFrameReplayed = true; return; }

        // TEMP trace: WHY this frame is walking instead of patching - the walk is O(scene) and the patch is O(dirty), so
        // every one of these is a 43 ms frame among 0.12 ms ones.
        if (Core.Diagnostics.FrameTrace.Enabled && LastBuildKind == RenderBuildKind.Partial)
        {
            _traceWhy = !_opsRecorded ? (byte)1
                : !_opsReplayable ? (byte)2
                : LastBuildTransformDirty ? (byte)3
                : !OpsMatchTransforms ? (byte)4
                : _partialSpliced ? (byte)5
                : (byte)6;   // the patch itself refused
        }

        // FROM HERE the frame WALKS: O(scene), where everything above is O(dirty). Timed as its own phase so a draw that
        // is expensive can say which of the two it was - the replay phases above stay at their last values on a walking
        // frame, and reading them as though they described it is how a walk reads as a cheap replay.
        var walk0 = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {

        var scissorNarrowed = false;   // whether the active scissor is currently narrower than fullScissor

        _recording = device != null;   // a device walk records its op stream for a later clean-frame replay
        if (_recording)
        {
            _ops.Clear();
            _layers.Clear();
            _openLayer = null; 
            _opsReplayable = true; 
            _rectSlotByUnit.Clear();
            _sdfSlotByUnit.Clear();
            _textRunByUnit.Clear();
            _texRunByUnit.Clear();
            _fractalKindByUnit.Clear();
            _fillSlotByUnit.Clear();
            _haloRunsByUnit.Clear();
            _unitsByBrush.Clear();
            _brushPaintBaked.Clear();
            _walkGroup = null;
            _walkVersion++;
            _nodeAllAware.Clear();
            _culledWhenRecorded.Clear();
            _nodeStragglers.Clear();   // recorded per walk, exactly like the answers above it
            _movedNodesBuf.Clear();   // a full walk re-bakes fresh node matrices - pending node moves are subsumed
            _movedOwnersBuf.Clear();  // ...and every mover's subtree along with them
            _movedOwners.Clear();
            // A bailed fast path skipped its memo flush, so clear the world memos (not the clip memo) before walking.
            _worldCache.Clear();
            _relWorldCache.Clear();
            _opacityChain.Clear();
            _opacitySlotCache.Clear();
            _fadeSlotJustCreated = false;   // THIS walk is the one that re-bakes the instances with the new slot index
        }

        // Text + item-background + instanced-fill batches: reset per frame. Device renders only - GPU-free tests skip batching.
        if (device != null)
        {
            // The BatchId is how a recorded op finds its way back to the arena that drew it (see ArenaOf) - the same
            // numbers ExecuteOps switches on, said once, here.
            _textBatch ??= new TextBatchCollector { BatchId = 2 };
            _rectBatch ??= new RectBatchCollector { BatchId = 0 };
            _ellipseBatch ??= new EllipseBatchCollector { BatchId = 1 };
            _polygonBatch ??= new RegularPolygonCollector { BatchId = 12 };
            _gradientRectBatch ??= new GradientRectCollector { BatchId = 3 };
            _gradientEllipseBatch ??= new GradientEllipseCollector { BatchId = 4 };
            _patternBatch ??= new PatternRectCollector { BatchId = 5 };
            _fractalBatch ??= new FractalRectCollector { BatchId = 6 };
            _canvasGridBatch ??= new CanvasGridCollector { BatchId = 14 };
            _inkBatch ??= new InkCollector { BatchId = 15 };
            _arrowBatch ??= new CanvasArrowCollector { BatchId = 16 };
            // Lazy, like the textured batch: a material owns a capture texture, and a tree without one should not pay
            // for it.
            if (_materialBatch != null) _materialBatch.BeginFrame(device);
            _textBatch.BeginFrame(device);
            _rectBatch.BeginFrame(device);
            _ellipseBatch.BeginFrame(device);
            _polygonBatch.BeginFrame(device);
            _gradientRectBatch.BeginFrame(device);
            _gradientEllipseBatch.BeginFrame(device);
            _patternBatch.BeginFrame(device);
            _fractalBatch.BeginFrame(device);
            _canvasGridBatch.BeginFrame(device);
            _inkBatch.BeginFrame(device);
            _arrowBatch.BeginFrame(device);
            // Created lazily (below, on the first textured fill) - but once it exists it needs its frame reset like any
            // other collector. Leaving it out is what made a nine-slice draw for exactly ONE frame and then vanish.
            _texRectBatch?.BeginFrame(device);
            _haloUnder?.BeginFrame(device);
            _haloOver?.BeginFrame(device);
            _haloLivingUnder?.BeginFrame(device);
            _haloLivingOver?.BeginFrame(device);
            _haloOverOwner = null;
            // Transform table: this frame's copy, and its address on the collectors that were just (re)created above.
        BeginTransformFrame(device);
            var sceneClean = LastBuildKind == RenderBuildKind.Clean;
            // Incremental upload: a Clean frame re-bakes byte-identical items into slots the buffers already hold, so Flush
            // skips the redundant upload (zero bytes move on an idle frame).
            if (InstancedFillCollector.Enabled)
            {
                _instanceBuffers ??= new GpuBufferManager(device);
                _instancedFill ??= new InstancedFillCollector(device, _instanceBuffers) { PrepareOverlay = PrepareOverlayForDraw };
                _instancedFill.TransformsAddress = _transformTable.DeviceAddress;   // instance VS fetches its slot matrix
                _instancedFill.Backdrop = _materialBatch;   // may be null: the material batch is made on first sight of one
                _instancedFill.BeginFrame();
                _instancedFill.SceneClean = sceneClean;
            }
            _batchOpen = false;
        }

        // CLONES: a prototype's subtree is drawn once per matrix instead of once at its own place. The subtree is a
        // CONTIGUOUS run of groups (paint rank is DFS order), so a clone run is "replay groups [start, end) under another
        // matrix" - the per-unit body below is untouched except for the one line that composes the clone into the world.
        IReadOnlyList<Matrix4x4F> cloneRun = null;
        var cloneStart = 0;
        var cloneEnd = 0;
        var cloneIndex = 0;
        var recordingBeforeClones = _recording;

        for (var groupIndex = 0; groupIndex < _groups.Count; groupIndex++)
        {
            var group = _groups[groupIndex];

            // TEMP hunt: the walk drawing content that is no longer in the visual tree. The instances it bakes are as
            // real as any other - they are issued with their segment's range every replayed frame - so if this fires,
            // the withdrawal is not the sweep's problem at all: the paint order is being handed a departed subtree.

            if (cloneRun == null && group.Clones is { Count: > 0 } clones)
            {
                cloneRun = clones;
                cloneStart = groupIndex;
                cloneEnd = CloneSubtreeEnd(groupIndex);
                cloneIndex = 0;
                _cloneMatrix = clones[0];
                // Clone runs are recorded but not patchable: patches map one slot per unit, a cloned unit owns many.
                group.NotBatchable("clones");
            }

            foreach (var unit in group.Units)
            {
            // Group boundary (recording walks): reset this group's spliced-patch records once per group (re-derived by the
            // draw decisions below). Boundary detection instead of an outer block keeps the hot loop flat.
            if (_recording && !ReferenceEquals(group, _walkGroup))
            {
                _walkGroup = group;
                _recordOrder = group.Order;
                // An EMPTY rect segment will begin with whatever group first puts a rect in it, and that is this one or a
                // later one - so keep moving the mark until something lands.
                if (_rectBatch == null || !_rectBatch.HasPending) _rectSegStart = group.Order;
                group.Runs.Clear();
                group.Arena = null;
                group.PatchableBatchedOnly = true;
                group.NotBatchableBecause = null;   // a fresh walk describes this group from scratch
                group.WalkVersion = _walkVersion;
            }

            // Text bakes from its component's brush snapshot, so refresh it once here for every bake path below.
            if (unit is TextRenderUnit walkText) walkText.RefreshColors();

            // World transform read ONCE (frame-memoized): the bounds-cull below and the GPU re-bake use the SAME value, so
            // the cull can't approve "inside" while the GPU draws the element elsewhere (the spill).
            // A clone COMPOSES onto it (never replaces it): the subtree keeps its own internal layout and the clone only
            // says where this copy goes. Substituting would collapse the whole subtree onto the clone's origin.
            var wt = World(unit.Component);
            if (_cloneMatrix.HasValue) wt = wt * _cloneMatrix.Value;

            var scissor = fullScissor;
            var clipped = false;
            if (device != null)
            {
                scissor = ResolveScissor(unit.Component, wt, fullScissor, out clipped, out var cull);
                // Owner entirely outside its clip (a virtualized item just off the viewport, content sliding out): draw
                // nothing, feed no batch. The per-surviving-unit scissor is set below.
                if (cull)
                {
                    // A culled unit draws nothing, so its motion node stays PATCHABLE (rewriting its matrix can't desync
                    // draws that don't exist). Without this, tilting off-viewport tiles (the tilt FIELD moves every tile,
                    // scrolled-out included) left their nodes un-aware -> every mouse frame bailed to a full walk.
                    if (_recording && NodeOf(unit.Component) is { } culledNode)
                        _nodeAllAware.TryAdd(culledNode.RenderId, true);

                    // Remembered for this stream's lifetime: bringing it into view needs a walk (CollectMovedSubtree).
                    if (_recording) _culledWhenRecorded.Add(unit);
                    continue;
                }
            }

            // Bake AND draw with the transform the cull approved: refresh RenderData ONCE here (the batches read its opacity
            // while baking, the per-unit path reuses it). Culled units returned above. Compose the effective alpha from the
            // frozen snapshot first, so batches (rru.FillOpacity) and per-unit renderers bake with the current opacity.
            unit.SetEffectiveOpacity(EffectiveOpacity(unit.Component));
            unit.SetFadeSlot(OpacitySlotOf(device, unit.Component));
            // The rounded clip as a SHAPE, for the per-unit draws that take it as a uniform (the batched families read
            // the same numbers from the table by slot). Written before Update so the draw sees this frame's clip.
            if (unit.RenderData != null)
            {
                var (clipBox, clipRadii) = RoundedClipShape(unit.Component, fullScissor);
                unit.RenderData.RoundedClipBox = clipBox;
                unit.RenderData.RoundedClipRadii = clipRadii;
            }
            unit.Update(wt, _projectionMatrix, _renderScale);

            // The unit's soft bands (aura / shadow), if it wears any. NOT an alternative to its fill - an addition, so it
            // is collected before the routing chain and drawn first at the flush, which is what puts it UNDER the fills.
            if (device != null && (HaloRectCollector.WantsBatch(unit.RenderData) || HaloLivingCollector.WantsBatch(unit.RenderData)))
            {
                // The band reaches PAST the element, so the overlap test uses the grown box: what it must not be drawn
                // under is whatever the band itself covers, not just what the element covers. A LIVING band wanders
                // further than its radius, so it answers for its own reach.
                var reach = System.Math.Max(HaloRectCollector.MaxReach(unit.RenderData.Halo),
                    HaloLivingCollector.MaxReach(unit.RenderData.LivingHalo));
                var bandBounds = LogicalBounds(unit.Component, wt).Inflate(reach);
                if (ClipGroupChanged(scissor, unit.Component) || OverlapsHigherLayer(-1, bandBounds))
                {
                    FlushBatches(device, fullScissor, ref scissorNarrowed);
                }
                // Both sides are collected HERE, before the fill - only their FLUSH positions differ, and the flush is
                // what decides paint order. Collecting the inner one later would mean finding this unit again.
                var under = CollectHalo(device, unit, wt, scissor, inner: false);
                var over = CollectHalo(device, unit, wt, scissor, inner: true);
                under |= CollectLivingHalo(device, unit, wt, scissor, inner: false);
                over |= CollectLivingHalo(device, unit, wt, scissor, inner: true);
                if ((under || over) && _recording)
                {
                    group.NotBatchable("halo");   // a band is not a rect slot; the fast-path patch can't reproduce it
                }
            }

            // Batches: item-background rects (lower layer) + text (upper layer), each one instanced draw. A clip-group
            // change (scissor, or the text atlas) flushes BOTH together (rect-under-text order); a non-batchable unit that
            // overlaps either flushes both first so it paints on top.
            if (device != null && unit is RectangleRenderUnit rru && _rectBatch.CanBatch(rru.RectPayload))
            {
                var rectBounds = LogicalBounds(unit.Component, wt);
                if (ClipGroupChanged(scissor, unit.Component) || OverlapsHigherLayer(0, rectBounds, unit.Component))   // 0 = rect layer
                    FlushBatches(device, fullScissor, ref scissorNarrowed);
                var bakeWorld = ResolveBake(device, unit.Component, wt, out var slot4Rect);
                FadeBySlot(unit);   // this pass reads the alpha from the slot - keep it out of the color
                if (_rectBatch.TryAdd(rru.RectPayload, bakeWorld, rru.FillOpacity, scissor, rectBounds, slot4Rect,
                        rru.FadeSlot, TagOf(group), RoundedClipSlot(unit.Component, fullScissor)))
                {
                    if (_recording)
                    {
                        var slot = _rectBatch.LastSlot;
                        _rectSlotByUnit[unit] = slot;   // for a later fast-path partial replay
                        IndexUnitBrush(unit.Component, unit, rru.RectPayload.LiveBrush);   // for a composited paint re-bake
                        NoteBatched(group, _rectBatch, slot);
                    }
                    _batchScissor = scissor;
                    _batchClip = ClipOwnerOf(unit.Component);
                    _batchOpen = true;
                    continue;
                }
                // Rejected (rotated/sheared world, or the instance buffer overflowed): a batchable rect built no per-unit
                // machinery, so build its body now and re-bake this frame's transform into it, then it draws below.
                rru.EnsureMachinery();
                unit.Update(wt, _projectionMatrix, _renderScale);
            }
            else if (device != null && unit is RectangleRenderUnit iru && _inkBatch.CanBatch(iru.RectPayload))
            {
                // INK: one rectangle becomes as many instances as the stroke has segments, which is what keeps a
                // drawing to one draw call rather than one per stroke.
                var inkBounds = LogicalBounds(unit.Component, wt);
                if (ClipGroupChanged(scissor, unit.Component) || OverlapsHigherLayer(9, inkBounds, unit.Component))
                    FlushBatches(device, fullScissor, ref scissorNarrowed);

                var inkBakeWorld = ResolveBake(device, unit.Component, wt, out var slot4Ink);
                FadeBySlot(unit);
                if (_inkBatch.TryAdd(iru.RectPayload, inkBakeWorld, iru.FillOpacity, scissor, inkBounds, slot4Ink,
                        iru.FadeSlot, RoundedClipSlot(unit.Component, fullScissor)))
                {
                    if (_recording)
                    {
                        NoteBatched(group, _inkBatch, _inkBatch.LastSlot);
                        IndexUnitBrush(unit.Component, unit, iru.RectPayload.LiveBrush);
                    }
                    _batchScissor = scissor;
                    _batchClip = ClipOwnerOf(unit.Component);
                    _batchOpen = true;
                    continue;
                }
                iru.EnsureMachinery();
                unit.Update(wt, _projectionMatrix, _renderScale);
            }
            else if (device != null && unit is RectangleRenderUnit aru && _arrowBatch.CanBatch(aru.RectPayload))
            {
                // AN ARROW or a plain LINE: one rectangle becomes one instance, and the fragment decides the whole
                // shape - shaft and both heads - from the two ends written into it.
                var arrowBounds = LogicalBounds(unit.Component, wt);
                if (ClipGroupChanged(scissor, unit.Component) || OverlapsHigherLayer(9, arrowBounds, unit.Component))
                    FlushBatches(device, fullScissor, ref scissorNarrowed);

                var arrowBakeWorld = ResolveBake(device, unit.Component, wt, out var slot4Arrow);
                FadeBySlot(unit);
                if (_arrowBatch.TryAdd(aru.RectPayload, arrowBakeWorld, aru.FillOpacity, scissor, arrowBounds,
                        slot4Arrow, aru.FadeSlot, RoundedClipSlot(unit.Component, fullScissor)))
                {
                    if (_recording)
                    {
                        NoteBatched(group, _arrowBatch, _arrowBatch.LastSlot);
                        IndexUnitBrush(unit.Component, unit, aru.RectPayload.LiveBrush);
                    }
                    _batchScissor = scissor;
                    _batchClip = ClipOwnerOf(unit.Component);
                    _batchOpen = true;
                    continue;
                }
                aru.EnsureMachinery();
                unit.Update(wt, _projectionMatrix, _renderScale);
            }
            else if (device != null && unit is RectangleRenderUnit cgru && _canvasGridBatch.CanBatch(cgru.RectPayload))
            {
                // The canvas GROUND: one quad whose every pixel decides for itself whether it is on a mark. Layer 8 -
                // it flushes after every other fill, so anything painted EARLIER that it overlaps has to be flushed out
                // of the way first, or the grid covers it.
                var gridBounds = LogicalBounds(unit.Component, wt);
                if (ClipGroupChanged(scissor, unit.Component) || OverlapsHigherLayer(8, gridBounds, unit.Component))
                    FlushBatches(device, fullScissor, ref scissorNarrowed);

                var gridBakeWorld = ResolveBake(device, unit.Component, wt, out var slot4Grid);
                FadeBySlot(unit);
                if (_canvasGridBatch.TryAdd(cgru.RectPayload, gridBakeWorld, cgru.FillOpacity, scissor, gridBounds,
                        slot4Grid, cgru.FadeSlot, RoundedClipSlot(unit.Component, fullScissor)))
                {
                    if (_recording)
                    {
                        NoteBatched(group, _canvasGridBatch, _canvasGridBatch.LastSlot);
                        IndexUnitBrush(unit.Component, unit, cgru.RectPayload.LiveBrush);
                    }
                    _batchScissor = scissor;
                    _batchClip = ClipOwnerOf(unit.Component);
                    _batchOpen = true;
                    continue;
                }
                cgru.EnsureMachinery();
                unit.Update(wt, _projectionMatrix, _renderScale);
            }
            else if (device != null && unit is RectangleRenderUnit grru && _gradientRectBatch.CanBatch(grru.RectPayload))
            {
                // A rounded rect with a LINEAR/RADIAL gradient fill: same SDF-batch family, different pass (the pixel shader
                // evaluates the gradient). Shares the clip group with the other batches.
                var gradRectBounds = LogicalBounds(unit.Component, wt);
                if (ClipGroupChanged(scissor, unit.Component) || OverlapsHigherLayer(2, gradRectBounds, unit.Component))   // 2 = gradient-rect layer
                    FlushBatches(device, fullScissor, ref scissorNarrowed);
                var gradBakeWorld = ResolveBake(device, unit.Component, wt, out var slot4Grad);
                FadeBySlot(unit);
                if (_gradientRectBatch.TryAdd(grru.RectPayload, gradBakeWorld, grru.FillOpacity, scissor, gradRectBounds, slot4Grad, grru.FadeSlot,
                        RoundedClipSlot(unit.Component, fullScissor)))
                {
                    if (_recording)
                    {
                        _sdfSlotByUnit[unit] = (SdfSlotKind.GradientRect, _gradientRectBatch.LastSlot);
                        NoteBatched(group, _gradientRectBatch, _gradientRectBatch.LastSlot);
                        IndexUnitBrush(unit.Component, unit, grru.RectPayload.LiveBrush);
                    }
                    _batchScissor = scissor;
                    _batchClip = ClipOwnerOf(unit.Component);
                    _batchOpen = true;
                    continue;
                }
                grru.EnsureMachinery();
                unit.Update(wt, _projectionMatrix, _renderScale);
            }
            else if (device != null && unit is EllipseRenderUnit eru && _ellipseBatch.CanBatch(eru.EllipsePayload))
            {
                var ellipseBounds = LogicalBounds(unit.Component, wt);
                if (ClipGroupChanged(scissor, unit.Component) || OverlapsHigherLayer(1, ellipseBounds, unit.Component))   // 1 = ellipse layer
                    FlushBatches(device, fullScissor, ref scissorNarrowed);
                var bakeWorld = ResolveBake(device, unit.Component, wt, out var slot4El);
                FadeBySlot(unit);
                if (_ellipseBatch.TryAdd(eru.EllipsePayload, bakeWorld, eru.FillOpacity, scissor, ellipseBounds, slot4El, eru.FadeSlot,
                        RoundedClipSlot(unit.Component, fullScissor)))
                {
                    if (_recording)
                    {
                        _sdfSlotByUnit[unit] = (SdfSlotKind.Ellipse, _ellipseBatch.LastSlot);
                        NoteBatched(group, _ellipseBatch, _ellipseBatch.LastSlot);
                        IndexUnitBrush(unit.Component, unit, eru.EllipsePayload.LiveBrush);
                    }
                    _batchScissor = scissor;
                    _batchClip = ClipOwnerOf(unit.Component);
                    _batchOpen = true;
                    continue;
                }
                // Rejected (rotated/sheared world, or the instance buffer overflowed): build the body now + re-bake.
                eru.EnsureMachinery();
                unit.Update(wt, _projectionMatrix, _renderScale);
            }
            else if (device != null && unit is RegularPolygonRenderUnit pru2 && _polygonBatch.CanBatch(pru2.PolygonPayload))
            {
                var polyBounds = LogicalBounds(unit.Component, wt);
                if (ClipGroupChanged(scissor, unit.Component) || OverlapsHigherLayer(1, polyBounds, unit.Component))
                    FlushBatches(device, fullScissor, ref scissorNarrowed);
                var bakeWorldPoly = ResolveBake(device, unit.Component, wt, out var slot4Poly);
                FadeBySlot(unit);   // this pass reads the alpha from the slot - keep the chain out of the color
                if (_polygonBatch.TryAdd(pru2.PolygonPayload, bakeWorldPoly, pru2.FillOpacity, scissor, polyBounds, slot4Poly,
                        RoundedClipSlot(unit.Component, fullScissor), pru2.FadeSlot))
                {
                    if (_recording)
                    {
                        // Recorded so the move path treats it as batched (HoldsInstances), not as a per-unit draw.
                        _sdfSlotByUnit[unit] = (SdfSlotKind.Polygon, _polygonBatch.LastSlot);
                        group.NotBatchable("polygonBatch");   // non-rect-batch draw -> not rect-splice-patchable
                        IndexUnitBrush(unit.Component, unit, pru2.PolygonPayload.LiveBrush);
                    }
                    _batchScissor = scissor;
                    _batchClip = ClipOwnerOf(unit.Component);
                    _batchOpen = true;
                    continue;
                }
                // Rejected (rotated/sheared world, or the instance buffer overflowed): build the body now + re-bake.
                pru2.EnsureMachinery();
                unit.Update(wt, _projectionMatrix, _renderScale);
            }
            else if (device != null && unit is RegularPolygonRenderUnit gpru && GradientRectCollector.WantsBatchPolygon(gpru.PolygonPayload))
            {
                // A polygon with a GRADIENT fill: the same instanced pass the gradient rect uses, the shape still a
                // distance field. Same collector, so the same layer - the two ride one segment.
                var gradPolyBounds = LogicalBounds(unit.Component, wt);
                if (ClipGroupChanged(scissor, unit.Component) || OverlapsHigherLayer(2, gradPolyBounds, unit.Component))   // 2 = gradient-rect layer
                    FlushBatches(device, fullScissor, ref scissorNarrowed);
                var gradPolyBake = ResolveBake(device, unit.Component, wt, out var slot4GradPoly);
                FadeBySlot(unit);
                if (_gradientRectBatch.TryAddPolygon(gpru.PolygonPayload, gradPolyBake, gpru.FillOpacity, scissor, gradPolyBounds, slot4GradPoly,
                        gpru.FadeSlot, RoundedClipSlot(unit.Component, fullScissor)))
                {
                    if (_recording)
                    {
                        _sdfSlotByUnit[unit] = (SdfSlotKind.GradientRect, _gradientRectBatch.LastSlot);   // so a MOVE re-points it
                        group.NotBatchable("gradientPolygon");
                        IndexUnitBrush(unit.Component, unit, gpru.PolygonPayload.LiveBrush);
                    }
                    _batchScissor = scissor;
                    _batchClip = ClipOwnerOf(unit.Component);
                    _batchOpen = true;
                    continue;
                }
                gpru.EnsureMachinery();
                unit.Update(wt, _projectionMatrix, _renderScale);
            }
            else if (device != null && unit is RegularPolygonRenderUnit ppru && PatternRectCollector.WantsBatchPolygon(ppru.PolygonPayload))
            {
                // A polygon with a PROCEDURAL fill (pattern or noise): the pattern pass, same layer as the rect and the
                // ellipse forms of it.
                var patPolyBounds = LogicalBounds(unit.Component, wt);
                if (ClipGroupChanged(scissor, unit.Component) || !_patternBatch.SameKind(PatternRectCollector.KindOf(ppru.PolygonPayload.Brush))
                    || OverlapsHigherLayer(4, patPolyBounds, unit.Component))   // 4 = pattern layer
                    FlushBatches(device, fullScissor, ref scissorNarrowed);
                var patPolyBake = ResolveBake(device, unit.Component, wt, out var slot4PatPoly);
                FadeBySlot(unit);   // the pattern passes read the chain from the slot now - keep it out of the colors
                if (_patternBatch.TryAddPolygon(ppru.PolygonPayload, patPolyBake, ppru.FillOpacity, scissor, patPolyBounds, slot4PatPoly, ppru.FadeSlot,
                        RoundedClipSlot(unit.Component, fullScissor)))
                {
                    if (_recording)
                    {
                        _sdfSlotByUnit[unit] = (SdfSlotKind.Pattern, _patternBatch.LastSlot);   // so a MOVE re-points it
                        group.NotBatchable("patternPolygon");
                    }
                    _batchScissor = scissor;
                    _batchClip = ClipOwnerOf(unit.Component);
                    _batchOpen = true;
                    continue;
                }
                ppru.EnsureMachinery();
                unit.Update(wt, _projectionMatrix, _renderScale);
            }
            else if (device != null && unit is RegularPolygonRenderUnit tpru && TextureBatchCollector.WantsBatchPolygon(tpru.PolygonPayload))
            {
                // A polygon whose fill is SAMPLED from a texture - a picture, a drawing, a live element. Same textured
                // pass, same one-texture-per-segment rule.
                var texPolyTexture = tpru.BrushTexture();
                if (texPolyTexture == null) continue;
                if (_texRectBatch == null)
                {
                    _texRectBatch = new TextureBatchCollector { BatchId = 7, TransformsAddress = _transformTable?.DeviceAddress ?? 0 };
                    _texRectBatch.BeginFrame(device);
                }
                var texPolyBounds = LogicalBounds(unit.Component, wt);
                if (ClipGroupChanged(scissor, unit.Component) || !_texRectBatch.SameTexture(texPolyTexture)
                    || OverlapsHigherLayer(6, texPolyBounds, unit.Component))   // 6 = textured layer
                {
                    FlushBatches(device, fullScissor, ref scissorNarrowed);
                }
                var texPolyBake = ResolveBake(device, unit.Component, wt, out var slot4TexPoly);
                FadeBySlot(unit);   // this pass reads the chain from the slot now - keep it out of the tint
                if (_texRectBatch.TryAddPolygon(tpru.PolygonPayload, texPolyBake, tpru.FillOpacity, scissor, texPolyBounds, texPolyTexture, slot4TexPoly, tpru.FadeSlot,
                        RoundedClipSlot(unit.Component, fullScissor)))
                {
                    if (_recording)
                    {
                        if (_texRectBatch.LastCount == 1) _sdfSlotByUnit[unit] = (SdfSlotKind.Texture, _texRectBatch.LastFirst);
                        group.NotBatchable("texturedPolygon");
                    }
                    _batchScissor = scissor;
                    _batchClip = ClipOwnerOf(unit.Component);
                    _batchOpen = true;
                    continue;
                }
                tpru.EnsureMachinery();
                unit.Update(wt, _projectionMatrix, _renderScale);
            }
            else if (device != null && unit is EllipseRenderUnit geru && _gradientEllipseBatch.CanBatch(geru.EllipsePayload))
            {
                // A full ellipse with a LINEAR/RADIAL gradient fill: gradient sibling of the solid ellipse SDF batch.
                var gradElBounds = LogicalBounds(unit.Component, wt);
                if (ClipGroupChanged(scissor, unit.Component) || OverlapsHigherLayer(3, gradElBounds, unit.Component))   // 3 = gradient-ellipse layer
                    FlushBatches(device, fullScissor, ref scissorNarrowed);
                var gradElBakeWorld = ResolveBake(device, unit.Component, wt, out var slot4GradEl);
                FadeBySlot(unit);
                if (_gradientEllipseBatch.TryAdd(geru.EllipsePayload, gradElBakeWorld, geru.FillOpacity, scissor, gradElBounds, slot4GradEl,
                        geru.FadeSlot, RoundedClipSlot(unit.Component, fullScissor)))
                {
                    if (_recording)
                    {
                        _sdfSlotByUnit[unit] = (SdfSlotKind.GradientEllipse, _gradientEllipseBatch.LastSlot);
                        NoteBatched(group, _gradientEllipseBatch, _gradientEllipseBatch.LastSlot);
                        IndexUnitBrush(unit.Component, unit, geru.EllipsePayload.LiveBrush);
                    }
                    _batchScissor = scissor;
                    _batchClip = ClipOwnerOf(unit.Component);
                    _batchOpen = true;
                    continue;
                }
                geru.EnsureMachinery();
                unit.Update(wt, _projectionMatrix, _renderScale);
            }
            else if (device != null && unit is EllipseRenderUnit peru && _patternBatch.CanBatchEllipse(peru.EllipsePayload))
            {
                // A full ellipse with a PROCEDURAL PATTERN/NOISE fill: routes into the SAME pattern SDF batch (self-AA, no
                // jagged tessellated edges), the shader branching to the ellipse SDF on the negative baked corner radius.
                // Same clip group + layer 4 as the pattern rect.
                var patElBounds = LogicalBounds(unit.Component, wt);
                if (ClipGroupChanged(scissor, unit.Component) || !_patternBatch.SameKind(PatternRectCollector.KindOf(peru.EllipsePayload.Brush))
                    || OverlapsHigherLayer(4, patElBounds, unit.Component))   // 4 = pattern layer
                {
                    FlushBatches(device, fullScissor, ref scissorNarrowed);
                }
                var patElBakeWorld = ResolveBake(device, unit.Component, wt, out var slot4PatEl);
                FadeBySlot(unit);   // see the pattern rect branch
                if (_patternBatch.TryAddEllipse(peru.EllipsePayload, patElBakeWorld, peru.FillOpacity, scissor, patElBounds, slot4PatEl, peru.FadeSlot,
                        RoundedClipSlot(unit.Component, fullScissor)))
                {
                    if (_recording)
                    {
                        _sdfSlotByUnit[unit] = (SdfSlotKind.Pattern, _patternBatch.LastSlot);   // so a MOVE re-points it
                        group.NotBatchable("patternEllipse");
                    }
                    _batchScissor = scissor;
                    _batchClip = ClipOwnerOf(unit.Component);
                    _batchOpen = true;
                    continue;
                }
                peru.EnsureMachinery();
                unit.Update(wt, _projectionMatrix, _renderScale);
            }
            else if (device != null && unit is RectangleRenderUnit mru && MaterialRectCollector.WantsBatch(mru.RectPayload))
            {
                var materialBounds = LogicalBounds(unit.Component, wt);
                var matSource = mru.BrushTexture();   // null unless the brush names a picture of its own
                if (OpenMaterialSegment(device, mru.RectPayload.Brush, matSource, materialBounds, unit.Component, scissor,
                        fullScissor, ref scissorNarrowed))
                {
                    var matBakeWorld = ResolveBake(device, unit.Component, wt, out var slot4Mat);
                    FadeBySlot(unit);   // the material pass reads the chain from the slot; keep it out of the color
                    if (_materialBatch.TryAdd(mru.RectPayload, matBakeWorld, mru.FillOpacity, scissor, materialBounds,
                            slot4Mat, mru.FadeSlot, matSource, RoundedClipSlot(unit.Component, fullScissor)))
                    {
                        if (_recording) _sdfSlotByUnit[unit] = (SdfSlotKind.Material, _materialBatch.LastSlot);   // see the pattern branch
                        CloseMaterialSegment(group, unit.Component, scissor);
                        continue;
                    }
                }

                mru.EnsureMachinery();
                unit.Update(wt, _projectionMatrix, _renderScale);
            }
            // An ELLIPSE or a regular POLYGON filled with a material. Same batch, same pass: the figure rides as a flag
            // in the record and the shader branches on it, exactly as the pattern fills do - so all three shapes get
            // the material rather than only the rectangle.
            else if (device != null && unit is EllipseRenderUnit meru
                     && MaterialRectCollector.WantsBatch(meru.EllipsePayload.Brush, meru.EllipsePayload.Pen))
            {
                var bounds = LogicalBounds(unit.Component, wt);
                var elSource = meru.BrushTexture();
                if (OpenMaterialSegment(device, meru.EllipsePayload.Brush, elSource, bounds, unit.Component, scissor,
                        fullScissor, ref scissorNarrowed))
                {
                    var bake = ResolveBake(device, unit.Component, wt, out var slot);
                    FadeBySlot(unit);
                    if (_materialBatch.TryAddEllipse(meru.EllipsePayload, bake, meru.FillOpacity, scissor, bounds,
                            slot, meru.FadeSlot, elSource, RoundedClipSlot(unit.Component, fullScissor)))
                    {
                        if (_recording) _sdfSlotByUnit[unit] = (SdfSlotKind.Material, _materialBatch.LastSlot);
                        CloseMaterialSegment(group, unit.Component, scissor);
                        continue;
                    }
                }

                meru.EnsureMachinery();
                unit.Update(wt, _projectionMatrix, _renderScale);
            }
            else if (device != null && unit is RegularPolygonRenderUnit mpru
                     && MaterialRectCollector.WantsBatch(mpru.PolygonPayload.Brush, mpru.PolygonPayload.Pen))
            {
                var bounds = LogicalBounds(unit.Component, wt);
                var polySource = mpru.BrushTexture();
                if (OpenMaterialSegment(device, mpru.PolygonPayload.Brush, polySource, bounds, unit.Component, scissor,
                        fullScissor, ref scissorNarrowed))
                {
                    var bake = ResolveBake(device, unit.Component, wt, out var slot);
                    FadeBySlot(unit);
                    if (_materialBatch.TryAddPolygon(mpru.PolygonPayload, bake, mpru.FillOpacity, scissor, bounds,
                            slot, mpru.FadeSlot, polySource, RoundedClipSlot(unit.Component, fullScissor)))
                    {
                        if (_recording) _sdfSlotByUnit[unit] = (SdfSlotKind.Material, _materialBatch.LastSlot);
                        CloseMaterialSegment(group, unit.Component, scissor);
                        continue;
                    }
                }

                mpru.EnsureMachinery();
                unit.Update(wt, _projectionMatrix, _renderScale);
            }
            else if (device != null && unit is RectangleRenderUnit pru && _patternBatch.CanBatch(pru.RectPayload))
            {
                // A rounded rect with a PROCEDURAL PATTERN fill (checkerboard/stripes/dots/grid): a new SDF-batch sibling,
                // its own pass evaluates the pattern per fragment. Shares the clip group with the other batches.
                var patternBounds = LogicalBounds(unit.Component, wt);
                if (ClipGroupChanged(scissor, unit.Component) || !_patternBatch.SameKind(PatternRectCollector.KindOf(pru.RectPayload.Brush))
                    || OverlapsHigherLayer(4, patternBounds, unit.Component))   // 4 = pattern layer
                {
                    FlushBatches(device, fullScissor, ref scissorNarrowed);
                }
                var patBakeWorld = ResolveBake(device, unit.Component, wt, out var slot4Pat);
                // The SDF pattern reads its alpha from the slot now (Anim.z), so the bake must not fold the chain into
                // c1/c2 as well - that was the doubling this stand caught: 0.34 where every neighbor sat at 0.55.
                FadeBySlot(unit);
                if (_patternBatch.TryAdd(pru.RectPayload, patBakeWorld, pru.FillOpacity, scissor, patternBounds, slot4Pat, pru.FadeSlot,
                        RoundedClipSlot(unit.Component, fullScissor)))
                {
                    if (_recording)
                    {
                        // WHERE its record sits, so a MOVE can re-point it in place. Without this the unit answered
                        // HoldsInstances = false and the move path read that as "a per-unit draw the replay re-points",
                        // which it is not: a dragged pattern stayed put until an unrelated full walk caught up. The
                        // same defect the polygon had, and the same cure.
                        _sdfSlotByUnit[unit] = (SdfSlotKind.Pattern, _patternBatch.LastSlot);
                        group.NotBatchable("patternRect");
                    }
                    _batchScissor = scissor;
                    _batchClip = ClipOwnerOf(unit.Component);
                    _batchOpen = true;
                    continue;
                }
                pru.EnsureMachinery();
                unit.Update(wt, _projectionMatrix, _renderScale);
            }
            else if (device != null && unit is RectangleRenderUnit fru && _fractalBatch.CanBatch(fru.RectPayload))
            {
                // A rounded rect with an escape-time FRACTAL fill (Julia/Mandelbrot): a new SDF-batch sibling, its own pass
                // iterates z=z²+c per fragment. Shares the clip group with the other batches; auto-morph is a shader-side
                // Time drift, so this batch is not paint/slot-patchable (no _sdfSlotByUnit entry) - a full walk re-records it.
                var fractalBounds = LogicalBounds(unit.Component, wt);
                if (ClipGroupChanged(scissor, unit.Component) || !_fractalBatch.SameKind(FractalRectCollector.KindOf(fru.RectPayload.Brush))
                    || OverlapsHigherLayer(5, fractalBounds, unit.Component))   // 5 = fractal layer
                {
                    FlushBatches(device, fullScissor, ref scissorNarrowed);
                }
                var fracBakeWorld = ResolveBake(device, unit.Component, wt, out var slot4Frac);
                FadeBySlot(unit);   // this pass reads the chain from the slot now - keep it out of the colors
                if (_fractalBatch.TryAdd(fru.RectPayload, fracBakeWorld, fru.FillOpacity, scissor, fractalBounds, slot4Frac,
                        RoundedClipSlot(unit.Component, fullScissor), fru.FadeSlot))
                {
                    if (_recording)
                    {
                        _sdfSlotByUnit[unit] = (SdfSlotKind.Fractal, _fractalBatch.LastSlot);   // see the pattern branch
                        _fractalKindByUnit[unit] = FractalRectCollector.KindOf(fru.RectPayload.Brush);   // the pass this slot was recorded under
                        group.NotBatchable("fractal");
                    }
                    _batchScissor = scissor;
                    _batchClip = ClipOwnerOf(unit.Component);
                    _batchOpen = true;
                    continue;
                }
                fru.EnsureMachinery();
                unit.Update(wt, _projectionMatrix, _renderScale);
            }
            else if (device != null && unit is RectangleRenderUnit xru && TextureBatchCollector.WantsBatch(xru.RectPayload))
            {
                // A rounded rect whose fill is SAMPLED from a texture (ImageBrush / NineSliceBrush). ONE texture per
                // segment, so a different source flushes the batch - the same rule the text batch follows for its atlas.
                // A source still decoding has no texture yet: draw nothing this frame and let the re-render pick it up.
                var texture = xru.BrushTexture();
                if (texture == null)
                {
                    continue;
                }
                // Created on FIRST use, not with the other collectors: its GPU ring is dead weight in the windows that
                // never draw a textured fill - which is most of them - and enough caches paying for it at once ran the
                // device out of memory.
                if (_texRectBatch == null)
                {
                    _texRectBatch = new TextureBatchCollector { BatchId = 7, TransformsAddress = _transformTable?.DeviceAddress ?? 0 };
                    _texRectBatch.BeginFrame(device);
                }
                var texBounds = LogicalBounds(unit.Component, wt);
                if (ClipGroupChanged(scissor, unit.Component) || !_texRectBatch.SameTexture(texture)
                    || OverlapsHigherLayer(6, texBounds, unit.Component))   // 6 = textured layer
                {
                    FlushBatches(device, fullScissor, ref scissorNarrowed);
                }
                var texBakeWorld = ResolveBake(device, unit.Component, wt, out var slot4Tex);
                FadeBySlot(unit);   // this pass reads the chain from the slot now - keep it out of the tint
                if (_texRectBatch.TryAdd(xru.RectPayload, texBakeWorld, xru.FillOpacity, scissor, texBounds, texture, slot4Tex, xru.FadeSlot,
                        RoundedClipSlot(unit.Component, fullScissor)))
                {
                    if (_recording)
                    {
                        // Where its records sit, so a MOVE re-points them instead of waiting for a walk (see the
                        // pattern branch). A picture is one record and a NINE-SLICE is nine, so what is remembered is
                        // the RUN, exactly as a text block remembers its glyph run.
                        _sdfSlotByUnit[unit] = (SdfSlotKind.Texture, _texRectBatch.LastFirst);
                        _texRunByUnit[unit] = (_texRectBatch.LastFirst, _texRectBatch.LastCount);
                        group.NotBatchable("texturedRect");
                    }
                    _batchScissor = scissor;
                    _batchClip = ClipOwnerOf(unit.Component);
                    _batchOpen = true;
                    continue;
                }
                xru.EnsureMachinery();
                unit.Update(wt, _projectionMatrix, _renderScale);
            }
            else if (device != null && unit is EllipseRenderUnit xeru && TextureBatchCollector.WantsBatchEllipse(xeru.EllipsePayload))
            {
                // A full ellipse whose fill is SAMPLED from a texture: the SAME textured batch and layer as the rect, the
                // shader branching to the ellipse SDF on the negative baked corner radius. Same texture-per-segment rule.
                var texEllTexture = xeru.BrushTexture();
                if (texEllTexture == null)
                {
                    continue;
                }
                if (_texRectBatch == null)
                {
                    _texRectBatch = new TextureBatchCollector { BatchId = 7, TransformsAddress = _transformTable?.DeviceAddress ?? 0 };
                    _texRectBatch.BeginFrame(device);
                }
                var texEllBounds = LogicalBounds(unit.Component, wt);
                if (ClipGroupChanged(scissor, unit.Component) || !_texRectBatch.SameTexture(texEllTexture)
                    || OverlapsHigherLayer(6, texEllBounds, unit.Component))   // 6 = textured layer
                {
                    FlushBatches(device, fullScissor, ref scissorNarrowed);
                }
                var texEllBakeWorld = ResolveBake(device, unit.Component, wt, out var slot4TexEll);
                FadeBySlot(unit);   // this pass reads the chain from the slot now - keep it out of the tint
                if (_texRectBatch.TryAddEllipse(xeru.EllipsePayload, texEllBakeWorld, xeru.FillOpacity, scissor, texEllBounds, texEllTexture, slot4TexEll, xeru.FadeSlot,
                        RoundedClipSlot(unit.Component, fullScissor)))
                {
                    if (_recording)
                    {
                        if (_texRectBatch.LastCount == 1) _sdfSlotByUnit[unit] = (SdfSlotKind.Texture, _texRectBatch.LastFirst);
                        group.NotBatchable("texturedEllipse");
                    }
                    _batchScissor = scissor;
                    _batchClip = ClipOwnerOf(unit.Component);
                    _batchOpen = true;
                    continue;
                }
                xeru.EnsureMachinery();
                unit.Update(wt, _projectionMatrix, _renderScale);
            }
            else if (device != null && unit is TextRenderUnit tru && tru.TextComponent is { } tc && _textBatch.CanBatch(tc, out var atlas))
            {
                if (ClipGroupChanged(scissor, unit.Component) || !_textBatch.SameAtlas(atlas))
                    FlushBatches(device, fullScissor, ref scissorNarrowed);
                // Glyphs pack node-local like rects, plus the unit's own placement (a Drawing's text run), which the
                // component-based batch cannot reach itself.
                var textBake = tru.Place(ResolveBake(device, unit.Component, wt, out var slot4Text));
                var textFirst = _textBatch.RetainedCount;
                FadeBySlot(unit);   // this pass reads the alpha from the slot now - keep the chain out of the color
                if (_textBatch.TryAdd(tc, textBake, slot4Text, unit.FadeSlot, scissor, atlas, LogicalBounds(unit.Component, wt),
                        RoundedClipSlot(unit.Component, fullScissor)))
                {
                    // NOT slot-blind any more. Text used to bake the opacity CHAIN into its glyph colors because its
                    // shader could not read the table twice, so a fading ancestor had to re-bake every glyph under it;
                    // now the glyph VS reads the fade slot like everyone else. Leaving it in that list would apply the
                    // fade TWICE - once in the color it re-bakes, once in the slot the shader reads.
                    if (_recording)
                    {
                        // SLOT-patchable while the glyph count holds; and when it does NOT, the run is what the splice
                        // re-issues - so the glyphs are noted as this group's run in the text arena, exactly as a rect
                        // group notes its own.
                        _textRunByUnit[unit] = (textFirst, _textBatch.RetainedCount - textFirst, atlas);
                        for (var g = textFirst; g < _textBatch.RetainedCount; g++) NoteBatched(group, _textBatch, g);
                    }
                    _batchScissor = scissor;
                    _batchClip = ClipOwnerOf(unit.Component);
                    _batchOpen = true;
                    continue;   // baked into the batch - drawn at the next flush (node-aware: no MarkNodeNotAware)
                }
                // else: rotated/sheared RELATIVE transform or overflow -> fall through to the per-block direct draw below
                // (which re-marks the node not-aware, exactly like a rejected rect)
            }
            else if (device != null && InstancedFillCollector.Enabled && unit is GeometryRenderUnit gru && _instancedFill.CanBatch(gru))
            {
                // General instanced fill (arbitrary tessellated geometry sharing a mesh): collect the fill and DEFER this
                // unit's fringe/stroke to the flush (drawn over the fill). A clip change flushes; the fill lands in its
                // natural z-layer (paint order), not all-at-once.
                if (ClipGroupChanged(scissor, unit.Component))
                    FlushBatches(device, fullScissor, ref scissorNarrowed);
                // A group draws every fill and only then every fringe, so an already-pending FRINGED shape could band this
                // one. Masking the fringes to the group's own coverage settles it - except once a frame has spent all
                // 255 marks, where closing the group stands in.
                if (_instancedFill.CoverageMarksExhausted
                    && _instancedFill.OverlapsPendingFringe(gru.Payload.Geometry.Bounds.TransformToAABB(gru.Place(wt))))
                {
                    FlushBatches(device, fullScissor, ref scissorNarrowed);
                }
                var fillBake = ResolveBake(device, unit.Component, wt, out var slot4Fill);
                FadeBySlot(unit);
                if (_instancedFill.TryAdd(gru, fillBake, scissor, LogicalBounds(unit.Component, wt), slot4Fill,
                        RoundedClipSlot(unit.Component, fullScissor)))
                {
                    gru.FillInstanced = true;
                    // WHERE this fill sits is a fact about the arena as it stands, so it is remembered on EVERY walk -
                    // the paint patch asks on frames that record nothing, which is exactly when it is asked. Without it
                    // IsSlotPatchable answered "no" for every Path, and one of them cost the frame a walk of the whole
                    // scene: measured at 200 refusals in 8 s on a faded subtree, 38 ms a frame against 0.5 patched.
                    if (_instancedFill.LastArena is { } fillArenaSlot)
                        _fillSlotByUnit[unit] = (fillArenaSlot, _instancedFill.LastSlot);

                    // Fill and fringe ride the slot; per-unit overlays are re-pointed at the flush (PrepareOverlay). The run
                    // is noted like any family's.
                    if (_recording && _instancedFill.LastArena is { } fillArena)
                    {
                        NoteBatched(group, fillArena, _instancedFill.LastSlot);
                        IndexUnitBrush(unit.Component, unit, gru.Payload.LiveBrush);
                    }
                    _batchScissor = scissor;
                    _batchClip = ClipOwnerOf(unit.Component);
                    _batchOpen = true;
                    continue;   // fill batched; fringe/stroke drawn at the flush, over the fill
                }
                // Rejected (no drawable mesh / instance buffer overflow): draw the whole unit per-unit (fill included).
                gru.FillInstanced = false;
            }
            else if (device != null && InstancedFillCollector.Enabled && unit is GeometryRenderUnit ggru && _instancedFill.CanBatchGradient(ggru))
            {
                // General instanced GRADIENT fill (arbitrary geometry, gradient pass): same path, the fill body is skipped
                // (FillInstanced) and its fringe/stroke draw at the flush.
                if (ClipGroupChanged(scissor, unit.Component))
                    FlushBatches(device, fullScissor, ref scissorNarrowed);
                var gradBake = ResolveBake(device, unit.Component, wt, out var slot4GradFill);
                FadeBySlot(unit);
                if (_instancedFill.TryAddGradient(ggru, gradBake, scissor, LogicalBounds(unit.Component, wt), slot4GradFill,
                        RoundedClipSlot(unit.Component, fullScissor)))
                {
                    ggru.FillInstanced = true;
                    // The fill rides the slot now; a per-unit overlay (its fringe, still per-unit here, or a stroke)
                    // bakes its transform at record time and is re-pointed at the flush - see PrepareOverlay.
                    if (_recording) group.NotBatchable("instancedGradientFill");
                    _batchScissor = scissor;
                    _batchClip = ClipOwnerOf(unit.Component);
                    _batchOpen = true;
                    continue;
                }
                ggru.FillInstanced = false;
            }
            else if (device != null && InstancedFillCollector.Enabled && MaterialRectCollector.Enabled
                     && unit is GeometryRenderUnit mgru && _instancedFill.CanBatchMaterial(mgru))
            {
                // A BACKDROP MATERIAL on authored geometry - an outline that arrives as triangles rather than as a
                // formula. Same instanced path as the pattern and textured fills, plus the region it will copy: only the
                // cache knows how a logical box lands in device pixels.
                if (ClipGroupChanged(scissor, unit.Component))
                    FlushBatches(device, fullScissor, ref scissorNarrowed);
                EnsureMaterialBatch(device);
                var matBounds = LogicalBounds(unit.Component, wt);
                var matMeshBake = ResolveBake(device, unit.Component, wt, out var slot4MatMesh);
                FadeBySlot(unit);
                if (_instancedFill.TryAddMaterial(mgru, matMeshBake, scissor, matBounds, slot4MatMesh,
                        MaterialCaptureRegion(matBounds, scissor, fullScissor), RoundedClipSlot(unit.Component, fullScissor)))
                {
                    mgru.FillInstanced = true;
                    if (_recording) group.NotBatchable("instancedMaterialFill");
                    _batchScissor = scissor;
                    _batchClip = ClipOwnerOf(unit.Component);
                    _batchOpen = true;
                    continue;
                }
                mgru.FillInstanced = false;
            }
            else if (device != null && InstancedFillCollector.Enabled && unit is GeometryRenderUnit pgru && _instancedFill.CanBatchPattern(pgru))
            {
                // General instanced PATTERN/NOISE fill (arbitrary geometry, pattern-fill pass): same path as the gradient
                // one - the fill body is skipped (FillInstanced) and the unit's fringe/stroke draw at the flush.
                if (ClipGroupChanged(scissor, unit.Component))
                    FlushBatches(device, fullScissor, ref scissorNarrowed);
                var patBake = ResolveBake(device, unit.Component, wt, out var slot4PatFill);
                FadeBySlot(unit);
                if (_instancedFill.TryAddPattern(pgru, patBake, scissor, LogicalBounds(unit.Component, wt), slot4PatFill,
                        RoundedClipSlot(unit.Component, fullScissor)))
                {
                    pgru.FillInstanced = true;
                    // As the gradient above: the fill rides the slot, the overlay is re-pointed at the flush.
                    if (_recording) group.NotBatchable("instancedPatternFill");
                    _batchScissor = scissor;
                    _batchClip = ClipOwnerOf(unit.Component);
                    _batchOpen = true;
                    continue;
                }
                pgru.FillInstanced = false;
            }
            else if (device != null && InstancedFillCollector.Enabled && unit is GeometryRenderUnit tgru && _instancedFill.CanBatchTextured(tgru))
            {
                // General instanced TEXTURED fill (arbitrary geometry, textured-fill pass): as the gradient and pattern
                // ones - the fill body is skipped (FillInstanced) and the unit's fringe/stroke draw at the flush.
                if (ClipGroupChanged(scissor, unit.Component))
                    FlushBatches(device, fullScissor, ref scissorNarrowed);
                var texBake = ResolveBake(device, unit.Component, wt, out var slot4TexFill);
                FadeBySlot(unit);
                if (_instancedFill.TryAddTextured(tgru, texBake, scissor, LogicalBounds(unit.Component, wt), slot4TexFill,
                        RoundedClipSlot(unit.Component, fullScissor)))
                {
                    tgru.FillInstanced = true;
                    if (_recording) group.NotBatchable("instancedTexturedFill");
                    _batchScissor = scissor;
                    _batchClip = ClipOwnerOf(unit.Component);
                    _batchOpen = true;
                    continue;
                }
                tgru.FillInstanced = false;
            }
            else if (device != null && (_rectBatch.Active || _ellipseBatch.Active || _gradientRectBatch.Active || _gradientEllipseBatch.Active || _patternBatch.Active || _fractalBatch.Active || _textBatch.Active || (_instancedFill?.Active ?? false) || (_materialBatch?.Active ?? false) || (_canvasGridBatch?.Active ?? false)))
            {
                // A non-batchable unit that overlaps any pending batch: flush them first so this unit paints OVER them, as
                // its later source order requires. Spatially disjoint units (a list's items) don't flush.
                var lb = LogicalBounds(unit.Component, wt);
                if (_rectBatch.OverlapsPending(lb) || _ellipseBatch.OverlapsPending(lb) || _gradientRectBatch.OverlapsPending(lb) || _gradientEllipseBatch.OverlapsPending(lb) || _patternBatch.OverlapsPending(lb) || _fractalBatch.OverlapsPending(lb) || _textBatch.OverlapsPending(lb) || (_instancedFill?.OverlapsPending(lb) ?? false)
                    || (_materialBatch?.OverlapsPending(lb) ?? false) || (_canvasGridBatch?.OverlapsPending(lb) ?? false))
                    FlushBatches(device, fullScissor, ref scissorNarrowed);
            }
            else if (device == null && unit is RectangleRenderUnit rruNoDev)
            {
                // No device = the overlay (popup / tooltip) path: each unit draws individually via unit.Render() below,
                // with NO batching. A batchable fill builds no per-unit machinery (it expects to be batched), so without
                // building it now its Render() draws NOTHING (a tooltip badge's background vanished). Build the body +
                // re-bake this frame's transform, exactly as the batch-rejected path does above.
                rruNoDev.EnsureMachinery();
                unit.Update(wt, _projectionMatrix, _renderScale);
            }
            else if (device == null && unit is EllipseRenderUnit eruNoDev)
            {
                eruNoDev.EnsureMachinery();
                unit.Update(wt, _projectionMatrix, _renderScale);
            }

            if (device != null)
            {
                if (clipped)
                {
                    device.SetScissors(scissor);
                    scissorNarrowed = true;
                    RecordScissor(scissor, unit.Component);
                }
                else if (scissorNarrowed)
                {
                    // First unclipped unit after a clipped one (or after a flush): restore the full window scissor.
                    device.SetScissors(fullScissor);
                    scissorNarrowed = false;
                    RecordScissor(fullScissor);
                }
            }

            // A per-unit draw bakes its world into RenderData - but it is recorded as its own op, so a replay can re-point
            // it (see ExecuteOps). It costs the group its rect-only slot patch, not the node its move.
            if (_recording) group.NotBatchable($"perUnitDraw<{unit.GetType().Name}>");
            RefreshOverlayFade(unit);   // the chain from the table, as the replay does it - see RefreshOverlayFade
            unit.Render();
            if (_recording) RecordOp(new RenderOp { Kind = RenderOpKind.Unit, Unit = unit, Order = _recordOrder });
            }

            // End of a clone run's subtree: rewind to its first group under the next matrix, or leave the run.
            if (cloneRun != null && groupIndex == cloneEnd - 1)
            {
                if (++cloneIndex < cloneRun.Count)
                {
                    _cloneMatrix = cloneRun[cloneIndex];
                    groupIndex = cloneStart - 1;
                }
                else
                {
                    cloneRun = null;
                    _cloneMatrix = null;
                    _recording = recordingBeforeClones;
                }
            }
        }

        _cloneMatrix = null;   // a run that ended on the last group leaves it set otherwise
        _recording = recordingBeforeClones;

        // Drain the tail batches (rects under fills under text), then leave the device on the full scissor for next pass.
        if (device != null) FlushBatches(device, fullScissor, ref scissorNarrowed);
        if (scissorNarrowed) { device.SetScissors(fullScissor); RecordScissor(fullScissor); }

        if (_recording)
        {
            _opsRecorded = true; _recording = false;
            // The transform + layout state this op stream was recorded AGAINST. A replay is faithful only while it holds.
            _opsMatrixVersion = _transformTable?.MatrixVersion ?? 0;
            _opsLayoutVersion = _transformTable?.LayoutMatrixVersion ?? 0;
            _layoutChangedSinceRecord = false;
        }

        }
        finally
        {
            DrawWalkMs = System.Diagnostics.Stopwatch.GetElapsedTime(walk0).TotalMilliseconds;
        }
    }

    // Batches flush bottom-up by layer, so a unit overlapping a pending higher-layer batch must flush it first to keep
    // paint order.
    private bool OverlapsHigherLayer(int layer, Rect lb, IUIComponent owner = null)
    {
        // Layer -1 is the halo band: it sits under EVERY fill, so a pending rect that overlaps it was painted earlier and
        // has to be flushed first - otherwise a panel's own background, batched before its child was reached, covers the
        // child's glow completely.
        if (layer < 0 && _rectBatch.OverlapsPending(lb))
        {
            return true;
        }

        // The INNER band sits above every fill, so anything below text that overlaps a pending one has to flush first -
        // otherwise a later element's fill would be drawn over a glow that belongs to the element before it.
        if (layer < 8 && !ReferenceEquals(owner, _haloOverOwner)
            && ((_haloOver != null && _haloOver.OverlapsPending(lb))
                || (_haloLivingOver != null && _haloLivingOver.OverlapsPending(lb))))
        {
            return true;
        }

        if (layer < 1 && _ellipseBatch.OverlapsPending(lb))
        {
            return true;
        }

        // ...AND ITS POLYGON SIBLING, which sits in the same fill layer and was the one batch this guard never asked
        // about. A rectangle drawn AFTER a polygon therefore never flushed, and the polygon - flushed later, as the
        // higher layer - landed on top of it whatever the scene said. On a plane where everything stands in one order
        // that reads as a polygon that ignores "bring to front" entirely, which is exactly how it was found.
        if (layer < 1 && (_polygonBatch?.OverlapsPending(lb) ?? false))
        {
            return true;
        }

        if (layer < 2 && _gradientRectBatch.OverlapsPending(lb))
        {
            return true;
        }

        if (layer < 3 && _gradientEllipseBatch.OverlapsPending(lb))
        {
            return true;
        }

        if (layer < 4 && _patternBatch.OverlapsPending(lb))
        {
            return true;
        }

        if (layer < 5 && _fractalBatch.OverlapsPending(lb))
        {
            return true;
        }

        if (layer < 6 && (_texRectBatch?.OverlapsPending(lb) ?? false))
        {
            return true;
        }

        if (layer < 7 && (_instancedFill?.OverlapsPending(lb) ?? false))
        {
            return true;
        }

        // The canvas grid flushes after every other fill, so earlier overlapping fills must flush first.
        if (layer < 8 && (_canvasGridBatch?.OverlapsPending(lb) ?? false))
        {
            return true;
        }

        // INK, drawn on the ground and after every other fill. Same rule as the grid above: anything batched alongside
        // that was painted EARLIER has to be flushed out first, or the ink covers it.
        if (layer < 9 && (_inkBatch?.OverlapsPending(lb) ?? false))
        {
            return true;
        }

        // ARROWS and plain lines, on the same layer and for the same reason as the ink beside them.
        if (layer < 9 && (_arrowBatch?.OverlapsPending(lb) ?? false))
        {
            return true;
        }

        // Materials also flush last (to copy a finished frame), so fills overlapping a pending material flush first.
        if (layer < 8 && (_materialBatch?.OverlapsPending(lb) ?? false))
        {
            return true;
        }

        if (layer < 8 && _textBatch.OverlapsPending(lb))
        {
            return true;
        }

        return false;
    }

    // Play this frame's composited animations for RIGHT NOW and push to GPU, without the loop thread or the property system
    // (see Compositor). Recompose ALL entries (Tick), then apply each by its channel.
    private void ApplyCompositedAnimations(IGraphicsDevice device)
    {
        _traceComposited = 0;
        if (device == null || _transformTable == null) return;
        if (!Compositor.Tick(_compositedBuf))   // recomposes matrices AND republishes paint snapshots
        {
            if (_compositedOwners.Count > 0) _compositedOwners.Clear();
            return;
        }

        _traceComposited = _compositedBuf.Count;

        _compositedOwners.Clear();
        foreach (var entry in _compositedBuf)
        {
            if (entry.Channel == CompositorChannel.Transform)
            {
                ApplyCompositedTransform(device, entry);
                if (entry.Owner != null) _compositedOwners.Add(entry.Owner);
            }
            else if (entry.Channel == CompositorChannel.Opacity) ApplyCompositedOpacity(device, entry);
            else ApplyCompositedPaint(device, entry);
        }
    }

    // OPACITY: the twin of the transform write below, four bytes instead of sixty-four. The element's alpha goes into its
    // opacity slot and every instance under it composes it at draw time, so a fading subtree costs one write however large
    // it is. The frozen SNAPSHOT is updated for the same reason the transform updates it: a walk that runs THIS frame must
    // agree with the compositor rather than re-bake the element back at its old alpha.
    private void ApplyCompositedOpacity(IGraphicsDevice device, Compositor.Entry entry)
    {
        if (entry.Owner is not { } owner || _transformTable == null) return;

        var slot = OpacitySlotOf(device, owner);
        if (slot < 0) return;   // not drawing, or its slot is not in this frame's buffer yet - the next walk links it

        _transformTable.SetAlpha(device, slot, entry.Alpha);

        // Keep the snapshot's own value in step, so EffectiveOpacity (which the slot-blind families still bake with)
        // reads the alpha the compositor just applied.
        if (_applySnap.TryGetValue(owner, out var snap))
            _applySnap[owner] = new LayoutSnapshot(snap.LocalTransform, snap.RenderSize, snap.ClipToBounds,
                snap.IsMotionNode, snap.RenderParent, entry.Alpha, snap.SelfOpacity, snap.ClipRadii);
    }

    // TRANSFORM: one 64-byte matrix write moves the whole node, and it lands in TWO places. The transform table is what the
    // GPU reads (the retained instances draw in the new place). The frozen SNAPSHOT is what every compose helper here reads
    // (World, NodeOf, ResolveScissor), so overwriting LocalTransform makes a walk that runs this frame agree with the
    // compositor instead of re-baking the element back where it was.
    private void ApplyCompositedTransform(IGraphicsDevice device, Compositor.Entry entry)
    {
        var owner = entry.Owner;

        LayoutSnapshot snap;
        Matrix4x4F parentWorld;
        if (_applySnap.TryGetValue(owner, out snap))
        {
            // Normal frame: the whole snapshot is present. Remember this owner's snapshot + parent world so the fallback
            // below can keep animating it while the snapshot is being re-captured.
            parentWorld = snap.RenderParent != null ? World(snap.RenderParent) : Matrix4x4F.Identity;
            _compositedFallback[owner] = (snap, parentWorld);
        }
        else if (_compositedFallback.TryGetValue(owner, out var fb))
        {
            // A SETTLING theme/DPI swap re-captures the ENTIRE snapshot every frame (SnapReset - the cascade writes outside
            // the mark system), so between re-captures the owner is briefly absent and the animation would freeze on screen
            // while the render thread still ticks it (the theme-swap spinner). Reuse the last parent world we saw for this
            // owner (a busy overlay spinner's ancestors don't move mid-swap) so it keeps turning until the snapshot settles.
            snap = fb.Snap;
            parentWorld = fb.ParentWorld;
        }
        else return;   // never applied yet (its motion-node promotion isn't recorded here); the loop mirror re-records.

        var world = snap.RenderParent != null ? entry.Local * parentWorld : entry.Local;
        _applySnap[owner] = new LayoutSnapshot(entry.Local, snap.RenderSize, snap.ClipToBounds, snap.IsMotionNode, snap.RenderParent, snap.Opacity, snap.SelfOpacity);
        _worldCache[owner] = world;   // set directly: the _applySnap chain may be mid-recapture, so don't recompose off it
        _transformTable.SetMatrix(device, _transformTable.AcquireSlot(owner.RenderId), world);
        entry.MarkApplied();   // tell the loop thread the render thread is drawing this - so its mirror stops re-baking it
    }

    // PAINT: Tick already republished the brush's snapshot; here every slot that paints with it is re-baked from that
    // snapshot. Nothing moved, so this is the SAME per-slot re-bake the loop-driven paint patch does, on the render thread.
    // One brush fans out to all its units (the skeleton pulse) - the reason paint needs the brush->units index.
    private void ApplyCompositedPaint(IGraphicsDevice device, Compositor.Entry entry)
    {
        if (!entry.PaintChanged) return;   // the baked bytes are identical to last present - nothing to do (see Entry)
        if (entry.Target is not Core.Media.Brush brush) return;
        if (!_unitsByBrush.TryGetValue(brush, out var units)) return;

        foreach (var u in units)
        {
            if (!IsSlotPatchable(u)) continue;   // a unit whose bytes moved off the slot map (rare) - the next walk fixes it
            var bakeWorld = ResolveBake(device, u.Component, World(u.Component), out var slot);
            PatchSlot(device, u, bakeWorld, slot);
        }
    }

    private void RecordScissor(Rect2D scissor, IUIComponent clip = null)
    {
        if (_recording) RecordOp(new RenderOp { Kind = RenderOpKind.Scissor, Scissor = scissor, Clip = clip, Order = _recordOrder });
    }

    // A recorded Scissor is a rect in WORLD space, and nothing re-derived it - which is why a move under a clip used to
    // cost the whole frame a re-record. Derive them again instead: each names the component its rect came from, and
    // CumulativeClip answers from the (already updated) frozen snapshot. There are tens of these ops in a frame against
    // tens of thousands of nodes in the scene, so this is the cheap end of the trade by three orders of magnitude.
    private void RefreshMaterialCaptureRegion(int segId, Rect2D limit, Rect2D fullScissor)
    {
        if (_materialBatch == null || !_matSegNode.TryGetValue(segId, out var recorded)) return;

        var now = World(recorded.Node);
        var dx = now.M41 - recorded.World.M41;
        var dy = now.M42 - recorded.World.M42;
        if (dx == 0 && dy == 0) return;

        var bounds = _materialBatch.SegmentBounds(segId);
        if (bounds.IsEmpty) return;

        // Measured from the FLUSH, never from the previous frame: the footprint below is the one the segment recorded and
        // does not move, so a per-frame increment would carry it by one step instead of the whole displacement.
        _materialBatch.SetSegmentRegion(segId,
            MaterialCaptureRegion(new Rect(bounds.X + dx, bounds.Y + dy, bounds.Width, bounds.Height), limit, fullScissor));
    }

    private void RefreshMovedScissors(Rect2D fullScissor)
    {
        // An ORDINARY mover carries viewports past too - a whole view sliding into place takes its scroll area with it -
        // and this used to ask only about NODES, so the one case left out was the expensive one: a tab body moving into
        // place clips, so its move was never forgiven and every switch cost a full walk of the scene.
        if (_movedNodeOwners.Count == 0 && _movedOwners.Count == 0) return;

        _clipCache.Clear();   // the viewports themselves moved - that memo is what went stale

        for (var i = 0; i < _ops.Count; i++)
        {
            if (_ops[i].Clip is not { } owner) continue;
            var op = _ops[i];
            var rect = CumulativeClip(owner) is { } logical ? ToFramebufferScissor(logical, fullScissor) : fullScissor;

            // THREE things hold a clip, not one, and missing any of them draws half the frame through the old viewport:
            // the Scissor op itself, the batch SEGMENT (which sets its own before drawing), and an instanced-fill FLUSH.
            switch (op.Kind)
            {
                case RenderOpKind.Scissor:
                    op.Scissor = rect;
                    _ops[i] = op;
                    break;
                case RenderOpKind.Segment:
                    ArenaOf(op.Batch)?.SetSegmentScissor(op.SegId, rect);
                    if (op.Batch == 13) RefreshMaterialCaptureRegion(op.SegId, rect, fullScissor);
                    break;
                case RenderOpKind.InstancedFlush:
                    _instancedFill?.SetFlushScissor(op.SegId, rect);
                    break;
            }
        }
    }



    /// <summary>Brush repaints served through <see cref="ApplyBrushRepaints"/>, for tests.</summary>
    internal int BrushRepaintTotal => _brushRepaintTotal;
    private int _brushRepaintTotal;

    // Re-bakes the slots of brushes rewritten in place since baking (palette repaints), via the brush index, on every
    // frame path.
    private void ApplyBrushRepaints(IGraphicsDevice device)
    {
        // Not during a SPLICE, for the same reason the paint patch stands aside: its whole business is moving the very
        // slots this would be writing. The splice re-issues those records from the payload anyway, so the color is not
        // lost - only this pass is.
        if (device == null || _partialSpliced || _brushPaintBaked.Count == 0) return;

        // The global paint epoch skips the O(brushes) scan on frames where nothing repainted.
        var epoch = Core.Media.Brush.PaintEpoch;
        if (epoch == _brushEpochSeen) return;
        _brushEpochSeen = epoch;

        _repaintedBrushes.Clear();
        foreach (var pair in _brushPaintBaked)
            if (pair.Key.PaintVersion != pair.Value)
                _repaintedBrushes.Add(pair.Key);

        if (_repaintedBrushes.Count == 0) return;
        _brushRepaintTotal += _repaintedBrushes.Count;

        foreach (var brush in _repaintedBrushes)
        {
            _brushPaintBaked[brush] = brush.PaintVersion;
            if (!_unitsByBrush.TryGetValue(brush, out var units)) continue;

            foreach (var u in units)
            {
                // A unit the patch cannot reach is repainted by the next walk, exactly as the composited paint path
                // treats one - refusing here would cost every OTHER unit of this brush its repaint.
                if (!IsSlotPatchable(u)) continue;

                u.SetFadeSlot(OpacitySlotOf(device, u.Component));
                u.SetEffectiveOpacity(EffectiveOpacity(u.Component));

                var bakeWorld = ResolveBake(device, u.Component, World(u.Component), out var slot);
                PatchSlot(device, u, bakeWorld, slot);
            }
        }
    }

    // Carries paint changes of dirty components into the arenas through PatchSlot, for units IsSlotPatchable accepts and
    // never during a splice.
    private void ApplyPaintToArenas(IGraphicsDevice device)
    {
        if (device == null || _partialDirty.Count == 0 || !_built) return;

        foreach (var comp in _partialDirty)
        {
            if (comp == null || !_groupById.TryGetValue(comp.RenderId, out var g)) continue;

            foreach (var u in g.Units)
            {
                // Text first and unconditionally: a color rewrite of its run cannot be refused, unlike the patch below.
                if (u is TextRenderUnit tru)
                {
                    tru.RefreshColors();
                    if (_textRunByUnit.TryGetValue(u, out var run))
                        _textBatch?.RecolorRun(device, run.First, run.Count, tru.TextComponent);
                    continue;
                }

                // Everything else re-bakes through the patch when accepted and not splicing; a refusal skips only that unit.
                if (_partialSpliced || !IsSlotPatchable(u)) continue;

                u.SetFadeSlot(OpacitySlotOf(device, u.Component));
                u.SetEffectiveOpacity(EffectiveOpacity(u.Component));

                var bakeWorld = ResolveBake(device, u.Component, World(u.Component), out var slot);
                PatchSlot(device, u, bakeWorld, slot);
            }
        }
    }

    // Patches the dirty units' batch slots and replays last frame's ops; false (full walk) if any dirty unit has no
    // patchable slot. Validates fully before patching.
    private bool TryPartialReplay(IGraphicsDevice device, Rect2D fullScissor)
    {
        // A patch repairs a frame whose PAINT ORDER is the one that was recorded: it writes remembered slot addresses
        // and replays that walk's stream. A group that has left or joined since invalidates both, and only rect
        // instances carry an owner tag to check (see PatchSlot) - the other families can only be answered by a walk.
        if (_leftTheOrder.Count > 0 || _orderJoined) return SpliceRefused("orderChanged");

        // Moved motion nodes first: rewrite their table matrices (64B each) so the replayed segments draw the scrolled
        // subtrees at their new position. A moved node with non-node-aware retained content bails to the full walk.
        if (!RefreshMovedNodes(device)) return SpliceRefused("movedNode");
        // ...then the ordinary movers, so the re-bakes below compose from the new worlds.
        if (!RefreshMovedComponents(device, fullScissor)) return SpliceRefused("movedComponent");
        RefreshMovedScissors(fullScissor);

        _opacityChain.Clear();   // a paint-only opacity change may have re-frozen the dirty subtree's snapshot; recompose it
        _opacitySlotCache.Clear();

        foreach (var comp in _partialDirty)
        {
            // A dirty component with NO drawn units (detached/pooled/collapsed - e.g. a text block that re-marks geometry
            // every frame but isn't in the paint tree) contributes nothing: the op stream is unchanged, so skip it and let
            // the replay stand. The common hover case (nothing visible changed).
            if (!_groupById.TryGetValue(comp.RenderId, out var g)) continue;

            foreach (var u in g.Units)
                if (!IsSlotPatchable(u))
                {
                    // A text block whose glyph COUNT moved is not patchable IN PLACE - its run is a fixed span - but it is
                    // repairable by re-issuing that run, which is what the splice does for any batched family. Nothing
                    // is mutated yet, so hand the frame over instead of walking the scene.
                    if (u is RenderUnits.TextRenderUnit && ReferenceEquals(g.Arena, _textBatch) && g.PatchableBatchedOnly)
                        return TrySplicedPatch(device, fullScissor);

                    // TEMP: name the type that costs the frame its patch.
                    // WHOSE unit, not just what kind: two text blocks refuse for different reasons and only the owner
                    // says which - the control's type and name are what a reproduction can be matched against.
                    if (Core.Diagnostics.FrameTrace.Enabled)
                        Core.Diagnostics.FrameTrace.Refuser = $"{u.GetType().Name}<{u.Component?.GetType().Name}>{Says(u)}{WhyNotPatchable(u)}";
                    return false;   // a per-unit / text / instanced / no-longer-batchable dirty unit -> full walk
                }
        }
        // Nothing moved on a geometry-only partial, so the cached world is still valid; re-bake each dirty tile from its
        // (just-updated) payload into its retained slot. (No-units components patched nothing above.)
        foreach (var comp in _partialDirty)
        {
            // Write the element's alpha into its opacity slot before the group lookup: fading containers own no units.
            OpacitySlotOf(device, comp);

            if (!_groupById.TryGetValue(comp.RenderId, out var g)) continue;
            foreach (var u in g.Units)
            {
                u.SetFadeSlot(OpacitySlotOf(device, u.Component));

                // Not skipped by family: any paint change arrives here, so slot readers are re-baked too. Pure fades
                // bypass this loop via ApplyCompositedOpacity.

                u.SetEffectiveOpacity(EffectiveOpacity(u.Component));   // a paint change may be an opacity change
                var bakeWorld = ResolveBake(device, u.Component, World(u.Component), out var slot);
                if (!PatchSlot(device, u, bakeWorld, slot))
                    return SpliceRefused($"notBakeable<{u.Component?.GetType().Name}>");   // rotated; the walk re-bakes anyway
            }
        }
        // A fade that just STARTED handed out a slot, and the instances under it still carry the index they were baked
        // with. Hand the frame to the walk so they pick it up - once, at the start of the fade; every step after it is
        // one float in the table.
        if (_fadeSlotJustCreated) return SpliceRefused("fadeSlotCreated");

        AcceptPatchedTransforms();
        ExecuteOps(device, fullScissor);
        return true;
    }

    // Is this element inside any of the dirty ones? Walks the RENDER parent chain of the frozen snapshot - the same
    // chain the opacity composes along, so "affected by that fade" and "found here" are the same question.
    private bool IsUnder(IUIComponent c, List<IUIComponent> roots)
    {
        for (var at = c; at != null; at = ApplySnap(at).RenderParent)
            foreach (var root in roots)
                if (ReferenceEquals(at, root)) return true;

        return false;
    }

    // Matrices a patch wrote were validated by it, so the stream adopts the new table and layout versions instead of
    // counting them as stale.
    private void AcceptPatchedTransforms()
    {
        _opsMatrixVersion = _transformTable?.MatrixVersion ?? 0;
        _opsLayoutVersion = _transformTable?.LayoutMatrixVersion ?? 0;
    }

    // Does this unit still occupy records in some arena? The slot maps are exactly that ledger.
    private bool HoldsInstances(IRenderUnit u) =>
        _rectSlotByUnit.ContainsKey(u) || _sdfSlotByUnit.ContainsKey(u)
        || _textRunByUnit.ContainsKey(u) || _fillSlotByUnit.ContainsKey(u);

    /// <summary>How many records the last walk gave this textured unit - 1 for a picture, 9 for a nine-slice. Units
    /// that predate the run map (or never took one) answer 1, which is what a single-slot entry means.</summary>
    private int TexRunLength(IRenderUnit u) => _texRunByUnit.TryGetValue(u, out var run) ? run.Count : 1;

    // Does THIS unit draw from a pass that reads the element's alpha from the opacity slot? The slot maps answer it: a
    // rect holds a rect-batch slot, a glyph run holds a text run, and the SDF map holds the rest - the polygon included,
    // since its record found room for the slot in the clip field.
    private bool ReadsSlotAlpha(IRenderUnit u) =>
        _rectSlotByUnit.ContainsKey(u)
        || _textRunByUnit.ContainsKey(u)
        || _sdfSlotByUnit.ContainsKey(u);

    // Does this arena's shader pass read the element's alpha from the opacity slot? Only these do; the rest still fold
    // the opacity chain into their color (see GlyphItem).
    private bool ReadsFadeSlot(BatchArena arena) =>
        ReferenceEquals(arena, _rectBatch) || ReferenceEquals(arena, _ellipseBatch)
        || ReferenceEquals(arena, _gradientRectBatch) || ReferenceEquals(arena, _gradientEllipseBatch)
        || arena is Retained.InstancedKeyArena;

    // Is this unit in the paint order at all? A group that is not draws nothing, so the walk never visits it and no slot
    // map holds its units - there is nothing to repaint and nothing to refuse over.
    private bool Drawing(IRenderUnit u) =>
        u.Component == null || (_groupById.TryGetValue(u.Component.RenderId, out var owner) && owner.InOrder);

    // Whether a unit can be repainted by rewriting its retained slot in place, without a walk.
    private bool IsSlotPatchable(IRenderUnit u)
    {
        // Not drawing and holding nothing: trivially done. Not drawing but still in an arena: needs blanking, which only
        // the splice or a walk does.
        if (!Drawing(u)) return !HoldsInstances(u);

        // Cloned units own a slot per clone, but the maps remember only one, so the next walk repaints them.
        if (u.Component?.RenderClones is { Count: > 0 }) return false;

        // A band that APPEARED or went dark is a change of record count in the halo arena, and a patch can only rewrite
        // records that are already there. Its own family may well still be patchable - the shape would repaint and the
        // band would not - so the whole unit takes the walk.
        if (!HaloRunStillDescribes(u)) return false;

        if (u is RectangleRenderUnit rru)
        {
            if (_rectSlotByUnit.ContainsKey(u)) return _rectBatch.CanBatch(rru.RectPayload);
            if (!_sdfSlotByUnit.TryGetValue(u, out var gr)) return false;
            // Each brush family owns a record in its own batch, and each answers for itself: a fill that stopped being
            // batchable this frame (a rotated world, an overflowed buffer) has no record to patch and its slot number
            // belongs to whoever took it.
            return gr.Kind switch
            {
                SdfSlotKind.GradientRect => _gradientRectBatch.CanBatch(rru.RectPayload),
                SdfSlotKind.Pattern => _patternBatch != null && _patternBatch.CanBatch(rru.RectPayload),
                // ...and, for a picture, the RUN must still be the same length: a nine-slice that became a plain fill
                // (or the other way round) is a change of record COUNT, which only the walk can express.
                SdfSlotKind.Texture => _texRectBatch != null && _texRectBatch.CanBatch(rru.RectPayload)
                                       && TextureBatchCollector.RecordCount(rru.RectPayload.Brush) == TexRunLength(u),
                // ...and the FORMULA (deep zoom included) must still be the one the segment bound its pass for: the patch
                // rewrites the record, not the pass. A deep one also has to still fit the reference orbit its slot owns -
                // that is decided at the patch itself, where the baked record can be measured against the slice.
                SdfSlotKind.Fractal => _fractalBatch != null && _fractalBatch.CanBatch(rru.RectPayload)
                                       && _fractalKindByUnit.TryGetValue(u, out var fracKind)
                                       && fracKind == FractalRectCollector.KindOf(rru.RectPayload.Brush),
                SdfSlotKind.Material => _materialBatch != null && _materialBatch.CanBatch(rru.RectPayload),
                _ => false
            };
        }

        // A text block holds a RUN of glyph slots. Patchable only while the run still DESCRIBES it: the same number of
        // glyphs (the run is a fixed span of the retained buffer) and the same atlas (the recorded segment binds one).
        // A counter ticking 600 -> 598 qualifies; text that grew or shrank does not, and takes the walk.
        if (u is TextRenderUnit tru && _textRunByUnit.TryGetValue(u, out var run))
            return tru.TextComponent is { } tc
                && _textBatch.CanBatch(tc, out var atlas)
                && atlas == run.Atlas
                && tc.GlyphRun.Count == run.Count;

        // A geometry unit whose FILL rides the instanced collector owns one record there, and that arena can re-bake a
        // record in place. Only while the fill is still instanced: a unit that fell back to a per-unit draw this frame
        // (rotated, or the buffer overflowed) has no record to patch, and its slot number belongs to whoever took it.
        if (u is GeometryRenderUnit fgru)
            return fgru.FillInstanced
                   && _fillSlotByUnit.ContainsKey(u)
                   && _instancedFill != null
                   && _instancedFill.CanBatch(fgru);

        // An ellipse or a polygon carries the SAME brush families a rectangle does - each in its own batch, each
        // answering for itself, exactly as the rect branch above.
        if (u is EllipseRenderUnit eru && _sdfSlotByUnit.TryGetValue(u, out var e))
            return e.Kind switch
            {
                SdfSlotKind.Ellipse => _ellipseBatch.CanBatch(eru.EllipsePayload),
                SdfSlotKind.GradientEllipse => _gradientEllipseBatch.CanBatch(eru.EllipsePayload),
                SdfSlotKind.Pattern => _patternBatch != null && _patternBatch.CanBatchEllipse(eru.EllipsePayload),
                SdfSlotKind.Texture => _texRectBatch != null && _texRectBatch.CanBatchEllipse(eru.EllipsePayload),
                SdfSlotKind.Material => _materialBatch != null && MaterialRectCollector.WantsBatch(eru.EllipsePayload.Brush, eru.EllipsePayload.Pen),
                _ => false
            };

        if (u is RegularPolygonRenderUnit pru && _sdfSlotByUnit.TryGetValue(u, out var p))
            return p.Kind switch
            {
                SdfSlotKind.Polygon => _polygonBatch.CanBatch(pru.PolygonPayload),
                SdfSlotKind.GradientRect => _gradientRectBatch.CanBatchPolygon(pru.PolygonPayload),
                SdfSlotKind.Pattern => _patternBatch != null && _patternBatch.CanBatchPolygon(pru.PolygonPayload),
                SdfSlotKind.Texture => _texRectBatch != null && _texRectBatch.CanBatchPolygon(pru.PolygonPayload),
                SdfSlotKind.Material => _materialBatch != null && MaterialRectCollector.WantsBatch(pru.PolygonPayload.Brush, pru.PolygonPayload.Pen),
                _ => false
            };

        return false;
    }

    // Re-bake one unit from its (live) payload straight into the slot it already occupies. Validated by IsSlotPatchable.
    private bool PatchSlot(IGraphicsDevice device, IRenderUnit u, Matrix4x4F bakeWorld, int transformSlot)
    {
        // Not in the paint order: nothing to repaint (a re-bake would resurrect a stale snapshot). Answer done, not refuse.
        ControlGroup owner = null;
        if (u.Component != null
            && (!_groupById.TryGetValue(u.Component.RenderId, out owner) || !owner.InOrder))
        {
            return true;
        }

        // The soft bands first: they are a SEPARATE record from the fill, so a repaint that touches only the fill left
        // a shape recolored and its aura on the old color until an unrelated frame walked the scene.
        PatchHalo(device, u, bakeWorld, transformSlot);

        if (u is RectangleRenderUnit rru)
        {
            // Only the SLOT-READING families are patched here (the maps below hold nothing else), so the color is
            // re-baked WITHOUT the chain - exactly as the walk bakes it.
            FadeBySlot(u);

            if (_rectSlotByUnit.TryGetValue(u, out var rectSlot))
            {
                // WHOSE slot is it NOW: writing into one that has been handed on paints that group with these bytes and
                // takes its tag, so the true owner goes blank and this control appears twice.
                if (!_rectBatch.SlotOwnedBy(rectSlot, owner?.Tag ?? 0)) return false;

                if (!RectBatchCollector.BakeItem(rru.RectPayload, bakeWorld, rru.FillOpacity, transformSlot, rru.FadeSlot, out var item)) return false;
                // The record the WALK writes carries its rounded clip (TryAdd, same line after the same bake). A patch
                // that leaves it out writes a record the walk would never have written: dragging a shape across a rounded
                // corner un-rounded the cut under it, and it came back only when something forced a walk.
                item.Clip = new Vector4F(RoundedClipSlot(u.Component, _frameScissor), 0, 0, 0);
                _rectBatch.UpdateSlot(device, rectSlot, item);
                return true;
            }

            var rectEntry = _sdfSlotByUnit[u];
            var rectClip = RoundedClipSlot(u.Component, _frameScissor);   // every family stamps the same clip the walk does

            // The BRUSH families, each re-baked into the record it already occupies. They reached this path late: until
            // they were noted in the slot map a moved pattern/picture/fractal/material stayed where it was until an
            // unrelated full walk caught up - the defect the polygon had before it was given a slot of its own.
            if (rectEntry.Kind == SdfSlotKind.Pattern)
            {
                if (!PatternRectCollector.BakeItem(rru.RectPayload, bakeWorld, rru.FillOpacity, transformSlot, rru.FadeSlot, rectClip, out var patItem)) return false;
                _patternBatch.UpdateSlot(device, rectEntry.Slot, patItem);
                return true;
            }

            if (rectEntry.Kind == SdfSlotKind.Texture)
            {
                // The whole RUN, in place: one record for a picture, nine for a nine-slice. Rewriting a PREFIX would
                // leave the rest of the frame painting the old place, so a length that no longer matches refuses and
                // the walk owns it.
                var texRun = _texRunByUnit.TryGetValue(u, out var tr) ? tr : (First: rectEntry.Slot, Count: 1);
                if (!TextureBatchCollector.BakeRun(rru.RectPayload, bakeWorld, rru.FillOpacity, transformSlot, rru.FadeSlot, rectClip, out var texItems, rru.BrushTexture())
                    || texItems.Length != texRun.Count)
                {
                    return false;
                }
                for (var i = 0; i < texItems.Length; i++) _texRectBatch.UpdateSlot(device, texRun.First + i, texItems[i]);
                return true;
            }

            if (rectEntry.Kind == SdfSlotKind.Fractal)
            {
                if (!FractalRectCollector.BakeItem(rru.RectPayload, bakeWorld, rru.FillOpacity, transformSlot, rectClip, rru.FadeSlot, out var fracItem)) return false;
                // BakeItem does not compute Ref - only a walk builds an orbit. A deep record therefore takes the one its
                // slot already owns, and refuses when that orbit no longer describes it.
                if (!_fractalBatch.TryStampOrbit(rectEntry.Slot, rru.RectPayload, ref fracItem)) return false;
                _fractalBatch.UpdateSlot(device, rectEntry.Slot, fracItem);
                return true;
            }

            if (rectEntry.Kind == SdfSlotKind.Material)
            {
                if (!MaterialRectCollector.BakeItem(rru.RectPayload, bakeWorld, rru.FillOpacity, transformSlot, rru.FadeSlot,
                        rru.BrushTexture(), rectClip, out var matItem)) return false;
                _materialBatch.UpdateSlot(device, rectEntry.Slot, matItem);
                return true;
            }

            if (!GradientRectCollector.BakeItem(rru.RectPayload, bakeWorld, rru.FillOpacity, transformSlot, rru.FadeSlot, out var gradItem)) return false;
            gradItem.Clip = new Vector4F(rectClip, 0, 0, 0);   // as the walk stamps it
            _gradientRectBatch.UpdateSlot(device, rectEntry.Slot, gradItem);
            return true;
        }

        if (u is TextRenderUnit tru)
        {
            // A paint patch is where an INHERITED recolor arrives: the block was never re-recorded, so its component
            // still holds the brushes it dereferenced at record time. Re-read them before baking.
            tru.RefreshColors();
            FadeBySlot(u);   // and bake the color WITHOUT the chain, exactly as the walk does - see the rect branch
            // The block's own placement rides on top of the bake, exactly as the recording walk composed it.
            return _textBatch.UpdateRun(device, _textRunByUnit[u].First, tru.TextComponent, tru.Place(bakeWorld), transformSlot, tru.FadeSlot,
                RoundedClipSlot(u.Component, _frameScissor));   // as the walk stamps it - see the rect branch
        }

        if (u is GeometryRenderUnit)
        {
            // Through the arena's own stage: it knows how to bake one record of its key, and the same two calls are what
            // the splice uses to replace a record. Staged, written, and the stage dropped - a patch owns nothing between
            // frames. The color keeps the opacity CHAIN here: this family's shader does not read the slot.
            var (fillArena, fillSlot) = _fillSlotByUnit[u];
            fillArena.ClearStage();
            if (!fillArena.TryStage(u, bakeWorld, transformSlot, 0, RoundedClipSlot(u.Component, _frameScissor))) return false;

            fillArena.UpdateSlotFromStage(device, fillSlot, 0);
            fillArena.ClearStage();
            return true;
        }

        if (u is RegularPolygonRenderUnit pru && _sdfSlotByUnit.TryGetValue(u, out var poly))
        {
            FadeBySlot(u);   // every family under this unit reads the alpha from the slot - bake without the chain
            var polyClip = RoundedClipSlot(u.Component, _frameScissor);
            switch (poly.Kind)
            {
                case SdfSlotKind.Polygon:
                    if (!RegularPolygonCollector.BakeItem(pru.PolygonPayload, bakeWorld, pru.FillOpacity, transformSlot, out var polyItem)) return false;
                    polyItem.Clip = new Vector4F(polyClip, pru.FadeSlot, 0, 0);   // both slots, as the walk stamps them
                    _polygonBatch.UpdateSlot(device, poly.Slot, polyItem);
                    return true;

                case SdfSlotKind.GradientRect:
                    if (!GradientRectCollector.BakePolygonItem(pru.PolygonPayload, bakeWorld, pru.FillOpacity, transformSlot, pru.FadeSlot, out var gPoly)) return false;
                    gPoly.Clip = new Vector4F(polyClip, 0, 0, 0);
                    _gradientRectBatch.UpdateSlot(device, poly.Slot, gPoly);
                    return true;

                case SdfSlotKind.Pattern:
                    if (!PatternRectCollector.BakePolygonItem(pru.PolygonPayload, bakeWorld, pru.FillOpacity, transformSlot, pru.FadeSlot, polyClip, out var patPoly)) return false;
                    _patternBatch.UpdateSlot(device, poly.Slot, patPoly);
                    return true;

                case SdfSlotKind.Texture:
                    if (!TextureBatchCollector.BakeSinglePolygon(pru.PolygonPayload, bakeWorld, pru.FillOpacity, transformSlot, pru.FadeSlot, polyClip, out var texPoly)) return false;
                    _texRectBatch.UpdateSlot(device, poly.Slot, texPoly);
                    return true;

                case SdfSlotKind.Material:
                    if (!MaterialRectCollector.BakePolygonItem(pru.PolygonPayload, bakeWorld, pru.FillOpacity, transformSlot, pru.FadeSlot,
                            pru.BrushTexture(), polyClip, out var matPoly)) return false;
                    _materialBatch.UpdateSlot(device, poly.Slot, matPoly);
                    return true;

                default:
                    return false;
            }
        }

        var eru = (EllipseRenderUnit)u;
        var entry = _sdfSlotByUnit[u];
        FadeBySlot(u);   // every ellipse family reads the alpha from the slot - see the rectangle above
        var elClip = RoundedClipSlot(u.Component, _frameScissor);
        switch (entry.Kind)
        {
            case SdfSlotKind.Ellipse:
                if (!EllipseBatchCollector.BakeItem(eru.EllipsePayload, bakeWorld, eru.FillOpacity, transformSlot, eru.FadeSlot, out var item)) return false;
                item.Params.Z = elClip;   // as the walk stamps it - see the rect branch
                _ellipseBatch.UpdateSlot(device, entry.Slot, item);
                return true;

            case SdfSlotKind.Pattern:
                if (!PatternRectCollector.BakeEllipseItem(eru.EllipsePayload, bakeWorld, eru.FillOpacity, transformSlot, eru.FadeSlot, elClip, out var patEl)) return false;
                _patternBatch.UpdateSlot(device, entry.Slot, patEl);
                return true;

            case SdfSlotKind.Texture:
                if (!TextureBatchCollector.BakeSingleEllipse(eru.EllipsePayload, bakeWorld, eru.FillOpacity, transformSlot, eru.FadeSlot, elClip, out var texEl)) return false;
                _texRectBatch.UpdateSlot(device, entry.Slot, texEl);
                return true;

            case SdfSlotKind.Material:
                if (!MaterialRectCollector.BakeEllipseItem(eru.EllipsePayload, bakeWorld, eru.FillOpacity, transformSlot, eru.FadeSlot,
                        eru.BrushTexture(), elClip, out var matEl)) return false;
                _materialBatch.UpdateSlot(device, entry.Slot, matEl);
                return true;

            default:
                if (!GradientEllipseCollector.BakeItem(eru.EllipsePayload, bakeWorld, eru.FillOpacity, transformSlot, eru.FadeSlot, out var gradEllipse)) return false;
                gradEllipse.Clip = new Vector4F(elClip, 0, 0, 0);   // as the walk stamps it
                _gradientEllipseBatch.UpdateSlot(device, entry.Slot, gradEllipse);
                return true;
        }
    }

    // Do the halo records the last walk gave this unit still describe it? Only the COUNT is asked - what the bands look
    // like is exactly what a patch rewrites; how many there are is what it cannot change.
    private bool HaloRunStillDescribes(IRenderUnit u)
    {
        // The SAME opacity the bake folds in, because the bake drops a band whose alpha reaches zero through it. Asking
        // without it, a faded shape counts bands the arena does not hold and the unit refuses the patch for good.
        var bands = u.RenderData.Halo;
        var opacity = u.RenderData.Opacity;

        // No run is not "unchanged": a unit gaining its first band has no run yet (NoteHaloRun), so it must not patch.
        if (_haloRunsByUnit.Count == 0 || !_haloRunsByUnit.TryGetValue(u, out var runs))
        {
            if (CountBands(bands, inner: false, opacity) != 0) return false;
            if (CountBands(bands, inner: true, opacity) != 0) return false;
            var appearing = u.RenderData.LivingHalo;
            return appearing is not { } band || band.Color.W * (float)opacity <= 0f;
        }
        if (CountBands(bands, inner: false, opacity) != runs.UnderCount) return false;
        if (CountBands(bands, inner: true, opacity) != runs.OverCount) return false;

        // Same question for the living band, and asked the same way: a band the bake would drop is a band the arena does
        // not hold, whatever the property still says.
        var live = u.RenderData.LivingHalo;
        var livingDraws = live is { } lb && lb.Color.W * (float)opacity > 0f;
        if ((livingDraws && live is { Inner: false } ? 1 : 0) != (runs.LivingUnder >= 0 ? 1 : 0)) return false;
        if ((livingDraws && live is { Inner: true } ? 1 : 0) != (runs.LivingOver >= 0 ? 1 : 0)) return false;

        return true;
    }

    // Records this side's bands take, by the bake's own test; alpha is ignored, since transparent bands keep their record.
    private static int CountBands(Core.Media.HaloBand[] bands, bool inner, double opacity)
    {
        if (bands == null) return 0;

        var n = 0;
        foreach (var band in bands)
        {
            if (band.IsEmpty || band.Inner != inner) continue;
            n++;
        }
        return n;
    }

    // Re-bake a unit's halo records in place, from the LIVE bands - the same bake the walk does, aimed at the records it
    // already owns. Silent about a unit that wears none (the common case: the map holds only shapes with a band).
    //
    // A band that has appeared or gone is NOT patched here: that is a change of record COUNT, which only the splice or a
    // walk can make - IsSlotPatchable refuses it, so this only ever rewrites what is already there.
    private void PatchHalo(IGraphicsDevice device, IRenderUnit u, Matrix4x4F bakeWorld, int transformSlot)
    {
        if (_haloRunsByUnit.Count == 0 || !_haloRunsByUnit.TryGetValue(u, out var runs)) return;
        if (!TryHaloShape(u, out var shape, out var corners, out var kind, out _, out var fieldRange)) return;

        FadeBySlot(u);   // as the walk bakes it - the band's chain comes from the slot, not from its color
        var opacity = u.RenderData.Opacity;
        var haloClip = RoundedClipSlot(u.Component, _frameScissor);   // the patch writes what the walk writes
        var haloFade = u.FadeSlot;
        PatchStillHalo(device, _haloUnder, u.RenderData.Halo, inner: false, runs.UnderFirst, runs.UnderCount,
            shape, corners, kind, bakeWorld, opacity, transformSlot, fieldRange, haloClip, haloFade);
        PatchStillHalo(device, _haloOver, u.RenderData.Halo, inner: true, runs.OverFirst, runs.OverCount,
            shape, corners, kind, bakeWorld, opacity, transformSlot, fieldRange, haloClip, haloFade);

        PatchLivingHalo(device, _haloLivingUnder, u.RenderData.LivingHalo, inner: false, runs.LivingUnder,
            shape, corners, kind, bakeWorld, opacity, transformSlot, fieldRange, haloClip, haloFade);
        PatchLivingHalo(device, _haloLivingOver, u.RenderData.LivingHalo, inner: true, runs.LivingOver,
            shape, corners, kind, bakeWorld, opacity, transformSlot, fieldRange, haloClip, haloFade);
    }

    private readonly HaloRectItem[] _haloPatchStage = new HaloRectItem[8];

    private void PatchStillHalo(IGraphicsDevice device, HaloRectCollector batch, Core.Media.HaloBand[] bands, bool inner,
        int first, int count, Rect shape, ProceduralGeometry.CornerRadius corners, HaloShape kind,
        Matrix4x4F bakeWorld, double opacity, int transformSlot, double fieldRange, int clipSlot, int fadeSlot)
    {
        if (batch == null || count <= 0 || bands == null) return;

        var room = System.Math.Min(count, _haloPatchStage.Length);
        var written = HaloRectCollector.BakeInto(_haloPatchStage.AsSpan(0, room), bands, inner, shape, corners, kind,
            bakeWorld, opacity, transformSlot, fieldRange, clipSlot, fadeSlot);

        // Fewer bands than the walk recorded means one went dark; the splice owns that, and rewriting a PREFIX here
        // would leave the rest painting the old color. Left to the refusal above.
        if (written != count) return;

        for (var i = 0; i < written; i++) batch.UpdateSlot(device, first + i, _haloPatchStage[i]);
    }

    private void PatchLivingHalo(IGraphicsDevice device, HaloLivingCollector batch, Core.Media.LivingBand? band, bool inner,
        int slot, Rect shape, ProceduralGeometry.CornerRadius corners, HaloShape kind,
        Matrix4x4F bakeWorld, double opacity, int transformSlot, double fieldRange, int clipSlot, int fadeSlot)
    {
        if (batch == null || slot < 0 || band is not { } live || live.Inner != inner) return;
        if (!HaloLivingCollector.BakeItem(live, shape, corners, kind, bakeWorld, opacity, live.Color, transformSlot,
                fieldRange, clipSlot, fadeSlot, out var item))
        {
            return;
        }

        batch.UpdateSlot(device, slot, item);
    }

    // One dirty group's staged patch (validated + baked BEFORE any mutation, so a bail leaves the retained frame intact).
    private struct GroupPatch
    {
        public ControlGroup Group;
        public BatchArena Arena;      // the family this group draws from - the one segment its repair happens in
        public int StageFirst;        // where its re-baked instances wait inside that arena's stage, in unit order
        public int StageCount;
        public Rect2D Scissor;        // the group's clip (all units of one component share it)
        public bool InPlace;          // count-stable recolor -> per-slot UpdateSlot, no surgery
        public bool Blank;            // stopped drawing -> zero its slots and KEEP the run, so coming back is an edit
        public IUIComponent Component;// whose it is - a blanked group has no units left to ask
        public int Layer;             // the layer this group's items belong to, resolved ONCE before anything is mutated
        public Rect Bounds;           // what it covers, in logical coordinates - the ONLY thing that decides whether its
                                      // order inside a layer matters at all (overlap is the merge rule)
    }

    // Draws a partial whose dirty controls changed their unit count by editing each one's layer in place and replaying:
    // O(dirty layer). Newcomers get their own layer by paint rank (PlaceNewSegment); all checks run before mutating.
    // The staging buffer is cleared on every exit, so refusals do not keep discarded controls alive.
    private bool TrySplicedPatch(IGraphicsDevice device, Rect2D fullScissor)
    {
        try
        {
            return TrySplicedPatchCore(device, fullScissor);
        }
        finally
        {
            _patchBuf.Clear();
        }
    }

    private bool TrySplicedPatchCore(IGraphicsDevice device, Rect2D fullScissor)
    {
        // Moved motion nodes first (same as TryPartialReplay): rewrite their matrices, bail on non-aware content.
        if (!RefreshMovedNodes(device)) return SpliceRefused("movedNode");
        if (!RefreshMovedComponents(device, fullScissor)) return SpliceRefused("movedComponent");
        RefreshMovedScissors(fullScissor);

        // Op stream grown too long from accumulated splices -> recompact with a full walk before it mis-replays.
        if (_ops.Count > MaxRetainedOps) return SpliceRefused("opsTooLong");

        _opacityChain.Clear();   // recompose from the (possibly re-frozen) snapshot, as in TryPartialReplay
        _opacitySlotCache.Clear();

        // ---- Validate + bake (no mutation) ----
        _patchBuf.Clear();
        _stagedArenas.Clear();
        var appendTotal = 0;
        foreach (var comp in _partialDirty)
        {
            OpacitySlotOf(device, comp);   // the fade itself, on the component - see TryPartialReplay

            if (!_groupById.TryGetValue(comp.RenderId, out var group)) continue;   // no drawn units - contributes nothing

            // The same refusal the paint order makes on the way in: a splice re-appends a group's content into the arena
            // and re-stamps its WalkVersion, so a departed subtree that reaches here is put BACK, frame after frame,
            // however faithfully the sweep blanks it. Two entrances into the arena, one rule about who may use them.
            if (LeftTheTree(group)) return SpliceRefused("departed");

            // ...or still in the tree but out of the paint order: skipped (not refused), or it would be re-baked back in.
            if (!group.InOrder) continue;

            // Runs are valid only for the last walk's arena; a group that walk skipped drops its runs (they may name other
            // groups' slots now) and re-appends.
            var walked = group.WalkVersion == _walkVersion;
            if (!walked) group.Runs.Clear();
            var runTotal = 0;
            foreach (var r in group.Runs) runTotal += r.Count;
            // A group DESCRIBED by the last walk must have drawn from ONE arena and nothing else - else it also drew
            // per-unit/text/instanced content whose recorded ops we can't excise (stale Unit ops would even replay
            // disposed units), or it is spread over two segments and there is no single one to repair.
            if (walked && !group.PatchableBatchedOnly)
                return SpliceRefused($"notOneArena<{comp.GetType().Name}>"
                                     + $" {group.NotBatchableBecause ?? "?"}"
                                     + (group.Units.Count > 0 ? Says(group.Units[0]) : ""));

            // WHICH arena repairs it. A group the walk described says so itself; a group that drew nothing yet is placed
            // into the arena its units would go to, which is decided by asking them to bake.
            var arena = group.Arena ?? ArenaFor(group);
            if (arena == null) return SpliceRefused($"noArena<{comp.GetType().Name}>");

            // Emptied ONCE per patch, not per group: two groups repairing the same family stage into the same buffer and
            // each owns the range it appended.
            if (_stagedArenas.Add(arena)) arena.ClearStage();
            var stageFirst = arena.StagedCount;
            var staged = 0;
            var scissor = fullScissor;
            var haveScissor = false;
            var bounds = Rect.Empty;
            foreach (var u in group.Units)
            {
                u.SetEffectiveOpacity(EffectiveOpacity(u.Component));   // a splice may ride an opacity cascade too
                u.SetFadeSlot(OpacitySlotOf(device, u.Component));
                var wt = World(u.Component);
                if (!haveScissor)
                {
                    scissor = ResolveScissor(u.Component, wt, fullScissor, out _, out var cull);
                    haveScissor = true;
                    if (cull) break;   // whole component off-clip: it contributes no items (units share the component)
                }
                var bakeWorld = ResolveBake(device, u.Component, wt, out var slot);
                // The families whose shaders read the opacity slot must be re-baked WITHOUT the chain in their color,
                // exactly as the walk bakes them; the rest keep it. Which arena repairs this group says which it is.
                if (ReadsFadeSlot(arena)) FadeBySlot(u);
                if (!arena.TryStage(u, bakeWorld, slot, TagOf(group), RoundedClipSlot(u.Component, fullScissor)))
                    return SpliceRefused($"notStageable<{u.GetType().Name} in {comp.GetType().Name}>");
                // How many INSTANCES it added, which is not how many units were asked: one rectangle is one instance, one
                // text block is a whole run of glyphs. Counting units here left a repaired block drawing its first glyph
                // and nothing else.
                staged = arena.StagedCount - stageFirst;
                bounds = bounds.IsEmpty ? LogicalBounds(u.Component, wt) : Union(bounds, LogicalBounds(u.Component, wt));
            }

            // Count-stable recolor (every unit already holds a retained slot): patch in place, no surgery/fragmentation.
            var inPlace = staged == group.Units.Count && staged == runTotal && AllUnitsHaveSlots(group);
            // It STOPPED drawing (hidden, or culled off its clip) but the frame still describes where it draws. Zero the
            // slots and leave the run alone: excising it costs its segment its shape and hands it a brand-new one - and a
            // new OP - the moment it comes back, which is a stream that only ever grows. A hovered close button flipping
            // hidden/shown took the stream from 29 ops to 64 and the replay from 0.68 to 1.54 ms.
            var blank = !inPlace && walked && staged == 0 && runTotal > 0;
            if (!inPlace && !blank) appendTotal += staged;
            _patchBuf.Add(new GroupPatch
            {
                Group = group, Arena = arena, StageFirst = stageFirst, StageCount = staged, Component = comp,
                Scissor = scissor, InPlace = inPlace, Blank = blank, Bounds = bounds
            });
        }

        // ---- Which LAYER does each surgery group belong to (no mutation) ----
        // The segment is re-baked whole and the same recorded op points at it, so op-stream order is untouched.
        _patchLayers.Clear();
        for (var n = 0; n < _patchBuf.Count; n++)
        {
            var p = _patchBuf[n];
            if (p.InPlace || p.Blank) continue;   // a blank keeps its run where it is, so it needs no layer resolved

            // Nothing recorded for this control yet: it gets its own segment, placed by its own paint rank.
            if (p.Group.Runs.Count == 0)
            {
                p.Layer = -1;
                _patchBuf[n] = p;
                continue;
            }

            var layer = TargetLayer(p.Group);
            if (layer < 0) return false;   // TargetLayer already named which of its three answers it gave
            // Its run names a segment NO RECORDED OP DRAWS - the stream was re-recorded without it (it was not drawing
            // when that walk went past). Runs that nothing draws describe nothing, which is the same situation as a group
            // the last walk never visited: drop them and give it a place of its own, instead of costing the whole frame a
            // walk over one control's stale bookkeeping.
            if (FindSegmentOp(p.Arena, layer) < 0)
            {
                p.Group.Runs.Clear();
                p.Layer = -1;
                _patchBuf[n] = p;
                continue;
            }
            // One segment draws under ONE clip; a group that now sits under a different one cannot join it. A layer whose
            // id is no longer part of the recorded frame has no clip to compare either - refuse rather than guess.
            var layerScissor = p.Arena.GetSegmentScissor(layer);
            if (layerScissor == null) return SpliceRefused("staleLayer");
            if (p.StageCount > 0 && !ScissorEquals(layerScissor, p.Scissor)) return SpliceRefused("otherClip");
            p.Layer = layer;
            _patchBuf[n] = p;
            _patchLayers.Add(layer);
        }

        // ---- Mutate (can no longer fail) ----
        foreach (var p in _patchBuf)
        {
            if (!p.InPlace) continue;   // count-stable recolor: the slots are already the right ones
            // In place only ever happens for a group whose every unit holds a RECT slot (AllUnitsHaveSlots asks that
            // map), so its arena is the rect one - said through the patch all the same, because that is where it is known.
            var i = 0;
            foreach (var u in p.Group.Units) p.Arena.UpdateSlotFromStage(device, _rectSlotByUnit[u], p.StageFirst + i++);
        }

        foreach (var p in _patchBuf)
        {
            if (!p.Blank) continue;
            foreach (var run in p.Group.Runs) 
                p.Arena.BlankSlots(device, run.First, run.Count);
            // The shape is gone; so must be its ink. A stroked path is nothing BUT ink.
            p.Arena.DropOverlayOf(p.Component);
            // Its runs are still ITS runs - kept current so the next patch repairs them instead of dropping them as a
            // stranger's slots. Units are left alone: staging nothing also means "culled off its clip", and those units
            // very much still exist.
            p.Group.WalkVersion = _walkVersion;
        }

        foreach (var p in _patchBuf)
        {
            if (p.InPlace || p.Blank) continue;
            if (!ReissueLayer(device, p)) return SpliceRefused("arenaFull");
        }

        AcceptPatchedTransforms();
        ExecuteOps(device, fullScissor);

        // Release the staging buffer now; its entries keep whole component subtrees alive until the next patch.
        _patchBuf.Clear();
        return true;
    }

    // TEMP: name WHICH of the splice's preconditions sent the frame to the full walk - there are nine and they are fixed
    // by nine different means.
    // TEMP: which of IsSlotPatchable's four conditions a text unit failed - "text refuses" is not a finding, and the
    // four have nothing to do with each other.
    private string WhyNotPatchable(IRenderUnit unit)
    {
        if (unit is not RenderUnits.TextRenderUnit tru) return string.Empty;
        if (!_textRunByUnit.TryGetValue(unit, out var run)) return " noRecordedRun";
        if (tru.TextComponent is not { } tc) return " noTextComponent";
        if (!_textBatch.CanBatch(tc, out var atlas)) return " cantBatch";
        if (atlas != run.Atlas) return " otherAtlas";
        if (tc.GlyphRun.Count != run.Count) return $" glyphs {run.Count}->{tc.GlyphRun.Count}";
        return " ?";
    }

    // TEMP: the first few characters a text unit draws. Two TextBlocks are the same type and the same name (none), and
    // what they SAY is the only thing that tells a diagnostics plate from a tab header.
    private static string Says(IRenderUnit unit)
    {
        if (unit is not RenderUnits.TextRenderUnit tru || tru.TextComponent?.TextLayout?.Text is not { } text) return string.Empty;
        return " \"" + (text.Length > 20 ? text[..20] : text).Replace('\n', '|') + "\"";
    }

    private static bool SpliceRefused(string reason)
    {
        if (Core.Diagnostics.FrameTrace.Enabled)
            Core.Diagnostics.FrameTrace.Refuser = reason;

        return false;
    }

    private static Rect Union(Rect a, Rect b)
    {
        var l = Math.Min(a.X, b.X);
        var t = Math.Min(a.Y, b.Y);
        var r = Math.Max(a.Right, b.Right);
        var bottom = Math.Max(a.Bottom, b.Bottom);
        return new Rect(l, t, r - l, bottom - t);
    }

    private static bool Overlaps(Rect a, Rect b) => a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;

    // Hand this group's glyph range back out to its blocks, in unit order - each takes as many slots as it has glyphs.
    // Anything that does not add up (a partly-culled group, a run that no longer covers them) gives its entries up rather
    // than keep a guess; the next walk restores them.
    private void ReslotTextRuns(ControlGroup group)
    {
        var total = 0;
        foreach (var run in group.Runs) total += run.Count;

        var glyphs = 0;
        foreach (var u in group.Units)
        {
            if (u is RenderUnits.TextRenderUnit { TextComponent: { } tc }) glyphs += tc.GlyphRun?.Count ?? 0;
        }

        if (group.Runs.Count != 1 || total != glyphs)
        {
            foreach (var u in group.Units) _textRunByUnit.Remove(u);
            return;
        }

        var at = group.Runs[0].First;
        foreach (var u in group.Units)
        {
            if (u is not RenderUnits.TextRenderUnit { TextComponent: { } tc }) continue;
            var count = tc.GlyphRun?.Count ?? 0;
            _textRunByUnit[u] = (at, count, tc.GlyphRun?.Atlas);
            at += count;
        }
    }

    private bool AllUnitsHaveSlots(ControlGroup group)
    {
        foreach (var u in group.Units)
            if (!_rectSlotByUnit.ContainsKey(u)) return false;
        return true;
    }

    // The op index that draws rect-batch segment <paramref name="segId"/>.
    private int FindSegmentOp(BatchArena arena, int segId)
    {
        for (var i = 0; i < _ops.Count; i++)
            if (_ops[i].Batch == arena.BatchId && arena.MatchesOp(_ops[i].Kind, _ops[i].SegId, segId)) return i;
        return -1;
    }

    // Arenas whose stage this patch has already emptied (see TrySplicedPatch).
    private readonly HashSet<BatchArena> _stagedArenas = new();

    // Readies the material batch for one more instance of any shape, flushing first if it cannot join. The batch is
    // created lazily and gets the transform table's address at construction, or its first frame reads a null address.
    private bool OpenMaterialSegment(IGraphicsDevice device, Core.Media.Brush brush, ITexture source, Rect bounds,
        IUIComponent component, Rect2D scissor, Rect2D fullScissor, ref bool scissorNarrowed)
    {
        if (device == null) return false;

        EnsureMaterialBatch(device);

        // Layer 7, the highest of the fills, on purpose: a material copies the frame behind it, so anything meant to
        // show THROUGH it has to have been drawn already. Overlapping a higher layer flushes, as everywhere else - and
        // here that rule is also what keeps two materials over each other from capturing the same stale frame.
        if (ClipGroupChanged(scissor, component)
            || !_materialBatch.SameKind(brush, source)   // one source AND one pass per segment
            || OverlapsHigherLayer(7, bounds, component))
        {
            FlushBatches(device, fullScissor, ref scissorNarrowed);
        }

        return true;
    }

    /// <summary>Make the material batch exist, lazily, on the first material of EITHER carrier (the meshes need it for
    /// its backdrop alone). The transform address is handed over AT CONSTRUCTION: without it the vertex shader
    /// dereferences NULL for a whole frame, and a bad address is not something validation sees - just a lost device.
    /// </summary>
    private void EnsureMaterialBatch(IGraphicsDevice device)
    {
        if (_materialBatch != null) return;

        _materialBatch = new MaterialRectCollector
        {
            BatchId = 13,
            TransformsAddress = _transformTable?.DeviceAddress ?? 0,
            WindowBoundsProvider = WindowOnDesktop
        };

        // ONLY on the frame it is created, because the frame's own BeginFrame pass has already gone by. Calling it
        // per material - which is what this used to do - resets Count and DISCARDS the segments recorded so far: an
        // acrylic pane followed by a mica one lost the acrylic entirely, since the second wiped the first before it
        // could be flushed.
        _materialBatch.BeginFrame(device);
        if (_instancedFill != null) _instancedFill.Backdrop = _materialBatch;
    }

    /// <summary>The frame region a material must copy to draw over: what it covers, grown so the blur has neighbors to
    /// average at its edges, then cut back to the clip it lives in - outside that clip is whatever is drawn OVER the
    /// element, and the blur would drag it inward as a dense band along the border.</summary>
    private Rect2D MaterialCaptureRegion(Rect bounds, Rect2D scissor, Rect2D fullScissor)
    {
        const int blurMargin = 24;
        var box = ToFramebufferScissor(bounds, fullScissor);
        return Intersect(new Rect2D
        {
            Offset = new Offset2D { X = box.Offset.X - blurMargin, Y = box.Offset.Y - blurMargin },
            Extent = new Extent2D
            {
                Width = box.Extent.Width + blurMargin * 2,
                Height = box.Extent.Height + blurMargin * 2
            }
        }, scissor);
    }

    /// <summary>Record that the pending segment now holds this instance - the clip it belongs to, and the fact that the
    /// group can no longer be patched as a plain batch.</summary>
    private void CloseMaterialSegment(ControlGroup group, IUIComponent component, Rect2D scissor)
    {
        if (_recording) group.NotBatchable("material");
        _batchScissor = scissor;
        _batchClip = component;
        _batchOpen = true;
    }

    // The arena a recorded Segment op draws from (the table ExecuteOps switches on); null for collectors never created.
    private BatchArena ArenaOf(byte batch) => batch switch
    {
        0 => _rectBatch,
        1 => _ellipseBatch,
        2 => _textBatch,
        3 => _gradientRectBatch,
        4 => _gradientEllipseBatch,
        5 => _patternBatch,
        6 => _fractalBatch,
        7 => _texRectBatch,
        8 => _haloUnder,
        9 => _haloOver,
        10 => _haloLivingUnder,
        11 => _haloLivingOver,
        12 => _polygonBatch,
        13 => _materialBatch,
        14 => _canvasGridBatch,
        15 => _inkBatch,
        16 => _arrowBatch,
        _ => _textBatch
    };

    /// <summary>Which arena would hold what this group draws, for a group the last walk never described (it drew nothing
    /// until now, so it has no arena of its own yet). Asked in the walk's own order of preference - a solid fill before
    /// its gradient form - so the patch puts it where a walk would have.</summary>
    private BatchArena ArenaFor(ControlGroup group)
    {
        if (group.Units.Count == 0) return _rectBatch;   // draws nothing: no arena is touched either way

        return group.Units[0] switch
        {
            RectangleRenderUnit rru => _rectBatch.CanBatch(rru.RectPayload) ? _rectBatch
                : _gradientRectBatch.CanBatch(rru.RectPayload) ? _gradientRectBatch : null,
            EllipseRenderUnit eru => _ellipseBatch.CanBatch(eru.EllipsePayload) ? _ellipseBatch
                : _gradientEllipseBatch.CanBatch(eru.EllipsePayload) ? _gradientEllipseBatch : null,
            RenderUnits.TextRenderUnit tru => tru.TextComponent is { } tc && _textBatch.CanBatch(tc, out _) ? _textBatch : null,
            RenderUnits.GeometryRenderUnit gru => _instancedFill?.ArenaFor(gru),
            _ => null
        };
    }

    // This group put one more instance into an arena: extend its contiguous run, or open a new one. A group draws from
    // ONE arena - that is what makes "repair the segment it lives in" a sentence at all - so a second family disqualifies
    // it from the splice, exactly as content in no arena does.
    private static void NoteBatched(ControlGroup group, BatchArena arena, int slot)
    {
        if (group.Arena == null) group.Arena = arena;
        else if (!ReferenceEquals(group.Arena, arena)) group.NotBatchable("twoArenas");

        var runs = group.Runs;
        if (runs.Count > 0 && runs[^1].First + runs[^1].Count == slot) runs[^1] = (runs[^1].First, runs[^1].Count + 1);
        else runs.Add((slot, 1));
    }

    private int TargetLayer(ControlGroup group)
    {
        var own = group.Arena.FindSegmentContaining(group.Runs[0].First);
        if (own < 0 && Core.Diagnostics.FrameTrace.Enabled) Core.Diagnostics.FrameTrace.Refuser = "runOutsideAnyLayer";
        return own;
    }

    // Re-issue ONE layer with one group's items replaced. The layer's content comes from the RETAINED RANGE, copied as
    // bytes, never rebuilt from the groups: group bookkeeping does not describe every slot in a layer (a culled unit, a
    // group the last walk did not visit), and whatever it fails to account for would be dropped. The range describes itself.
    private bool ReissueLayer(IGraphicsDevice device, GroupPatch patch)
    {
        if (patch.Layer < 0) return PlaceNewSegment(device, patch);

        var layer = patch.Layer;
        var arena = patch.Arena;
        var (first, count) = arena.SegmentRange(layer);
        if (first < 0) return false;   // the layer is gone from this recorded frame; the walk rebuilds it
        var scissor = arena.GetSegmentScissor(layer);
        var group = patch.Group;

        // WHERE inside the layer this group's items sit: its own run, which is the only reason it is in this layer at all.
        var at = group.Runs[0].First - first;
        var replaced = 0;
        foreach (var run in group.Runs) replaced += run.Count;

        // Another patch this frame split the layer under this run, so refuse and let the walk draw it
        // (BorderPatchRenderTests.TwoPatchesInOneFrame_AroundASplit).
        if (at < 0 || at + replaced > count) return SpliceRefused("runOutsideLayerAfterSplit");

        // The cheap path: edit inside the room the layer already owns, moving only what follows the edit. Only when the
        // layer has outgrown its room does it relocate, and then it does have to be carried across whole.
        if (!arena.ReplaceStagedInSegment(device, layer, at, replaced, patch.StageFirst, patch.StageCount))
        {
            // Outgrew the room this layer owns: it is carried to the end of the arena and every group drawing in it is
            // re-indexed below. THE cost a per-layer arena exists to remove.
            if (!arena.RepointSegmentAroundStage(device, layer, first, at, replaced, count, scissor, patch.StageFirst, patch.StageCount))
                return false;
        }

        // The layer may have moved, and everything after the edit shifted by the size difference. Re-index every group
        // that draws in it - runs and unit slots both, or a later patch would address freed space.
        // The layer now covers what this patch put into it, and the next placement has to see that.
        arena.GrowSegmentBounds(layer, patch.Bounds);
        var (newFirst, _) = arena.SegmentRange(layer);
        var delta = patch.StageCount - replaced;
        var editEnd = first + at + replaced;
        foreach (var g in _groups)
        {
            if (g.WalkVersion != _walkVersion || ReferenceEquals(g, group)) continue;
            // ...groups of THIS arena only. A slot number means something solely inside the array it indexes, and with a
            // family per arena two of them overlap numerically all the time - so without this a re-issue silently
            // re-pointed the runs of an unrelated family whose numbers happened to fall in the same range, and whatever
            // it drew stayed on screen as it was (the close button's highlight stuck lit).
            if (!ReferenceEquals(g.Arena, arena)) continue;
            var touched = false;
            for (var r = 0; r < g.Runs.Count; r++)
            {
                var run = g.Runs[r];
                if (run.First < first || run.First >= first + count) continue;
                g.Runs[r] = (run.First - first + newFirst + (run.First >= editEnd ? delta : 0), run.Count);
                touched = true;
            }

            if (touched) ReslotUnits(g);
        }

        group.Runs.Clear();
        group.WalkVersion = _walkVersion;
        if (patch.StageCount > 0)
        {
            group.Runs.Add((newFirst + at, patch.StageCount));
            group.Arena = arena;
            group.PatchableBatchedOnly = true;
            group.NotBatchableBecause = null;   // repaired into one arena: the old reason no longer describes it
        }

        ReslotUnits(group);
        return true;
    }

    // A control that drew nothing until now: give it its own segment and put its op where its RANK says, not where its
    // neighbors happen to be. Nothing already recorded moves, so this cannot disturb anyone's order - and it is provable
    // without looking at what else the frame contains.
    private bool PlaceNewSegment(IGraphicsDevice device, GroupPatch patch)
    {
        var group = patch.Group;
        if (patch.StageCount == 0)
        {
            group.WalkVersion = _walkVersion;
            ReslotUnits(group);
            return true;   // drew nothing, still draws nothing
        }

        // A recorded segment that paints ACROSS this rank is a LAYER: a set of draws whose mutual order
        // does not matter, because nothing in it overlaps anything else in it. So the newcomer only needs a place of its own
        // when it OVERLAPS what that layer draws - then order decides what covers what, and the layer has to be cut at its
        // rank. When it does not overlap, there is nothing to decide: it joins the layer and the segment stays whole.
        SplitSegmentSpanningRank(group.Order, patch.Bounds);

        var arena = patch.Arena;
        var seg = arena.AllocateSegmentFromStage(device, patch.Scissor, patch.StageFirst, patch.StageCount);
        if (seg < 0) return false;

        var at = OpIndexForRank(group.Order);
        _ops.Insert(at, new RenderOp
        {
            Kind = arena.OpKind, Batch = arena.BatchId, SegId = seg, Clip = group.Component, Order = group.Order
        });
        NoteOpInserted(at);

        arena.GrowSegmentBounds(seg, patch.Bounds);
        var (first, _) = arena.SegmentRange(seg);
        group.Runs.Clear();
        group.Runs.Add((first, patch.StageCount));
        group.Arena = arena;
        group.PatchableBatchedOnly = true;
        group.NotBatchableBecause = null;   // placed into one arena: the old reason no longer describes it
        group.WalkVersion = _walkVersion;
        ReslotUnits(group);
        return true;
    }

    // Before the first op of a later rank, never right after a Scissor op (it clips the draw that follows). A rank inside
    // a segment's span has no correct place until the segment is split (SplitSegmentSpanningRank).
    private int OpIndexForRank(long order)
    {
        // Ask the LAYERS first: they are the frame's structure, ordered and non-overlapping, so the rank picks one of
        // them - and only that layer's own ops have to be looked at. A scan of the whole stream answered the same
        // question by reading every op of every layer, including the ones whose ranks say nothing about this one.
        var from = 0;
        var to = _ops.Count;
        foreach (var layer in _layers)
        {
            if (layer.OpCount == 0) continue;
            if (order > layer.RankLast) continue;      // strictly earlier than the newcomer - keep going

            // The first layer that reaches this rank: either it covers it (place INSIDE, by rank) or it begins after it
            // (place before the whole layer).
            from = layer.OpFirst;
            to = layer.Covers(order) ? layer.OpFirst + layer.OpCount : layer.OpFirst;
            break;
        }

        var at = to;
        for (var i = from; i < to; i++)
        {
            if (_ops[i].Order <= order) continue;
            at = i;
            break;
        }

        // Never straight after a Scissor op: that op sets up the draw that FOLLOWS it, and slipping in between the two
        // would draw the newcomer under somebody else's clip.
        while (at > from && _ops[at - 1].Kind == RenderOpKind.Scissor) at--;
        return at;
    }

    // Splits the rect segment spanning `order` at the first slot owned by a later-ranked group, so a newcomer of that rank
    // has a place in the stream; no bytes move.
    private void SplitSegmentSpanningRank(long order, Rect newcomer)
    {
        for (var i = 0; i < _ops.Count; i++)
        {
            var op = _ops[i];
            if (op.Kind != RenderOpKind.Segment) continue;
            var spanned = ArenaOf(op.Batch);
            if (spanned == null) continue;
            if (op.OrderFirst >= order || order >= op.Order) continue;   // does not span this rank

            // THE MERGE RULE, and the whole point of a layer: what this segment draws is mutually order-independent, so a
            // newcomer that does not touch any of it is order-independent with it too. Cutting then would buy nothing and
            // cost a segment - which is what a rank-only test did on every single placement.
            var covered = spanned.SegmentBounds(op.SegId);
            if (!newcomer.IsEmpty && !covered.IsEmpty && !Overlaps(covered, newcomer))
            {
                LayerProbe.SplitsAvoided++;
                continue;
            }

            var cut = FirstSlotPaintedAfter(spanned, op.SegId, order);
            if (cut < 0) continue;   // its whole content paints BEFORE the newcomer after all - nothing to cut

            var second = spanned.SplitSegment(op.SegId, cut);
            if (second < 0) continue;
            LayerProbe.Splits++;

            // Nothing to fix up: every op, every pending patch and every layer this frame resolved names its segment by
            // ID, and the split gave the new half an id of its own. This is what used to be three synchronized loops over
            // the op stream, the patch buffer and its resolved layers - and the bug when one of them was missed.
            var spanEnd = op.Order;
            op.Order = order;   // this half now ends before the newcomer
            _ops[i] = op;
            _ops.Insert(i + 1, new RenderOp
            {
                Kind = RenderOpKind.Segment, Batch = op.Batch, SegId = second, Clip = op.Clip,
                Order = spanEnd, OrderFirst = order
            });
            NoteOpInserted(i + 1);
            return;   // one rank cuts one segment: the halves no longer span it
        }
    }

    // The first slot inside this segment that belongs to a group painting AFTER the given rank, or -1 if none does. Only
    // groups the current walk described can answer - a stale group's runs point at slots that have since been reassigned.
    private int FirstSlotPaintedAfter(BatchArena arena, int segId, long order)
    {
        var (first, count) = arena.SegmentRange(segId);
        var cut = -1;
        // From the first group ranked AFTER the newcomer, found by bisection - `_groups` is sorted by rank, and everything
        // before that point is answered by the rank alone. Scanning from the front asked the whole scene a question it had
        // already answered, once per placement, and placements are now on the CHEAP path.
        for (var i = FirstGroupAfter(order); i < _groups.Count; i++)
        {
            var group = _groups[i];
            if (group.WalkVersion != _walkVersion) continue;
            if (!ReferenceEquals(group.Arena, arena)) continue;   // a slot number only means something inside its own arena
            foreach (var run in group.Runs)
            {
                if (run.First < first || run.First >= first + count) continue;
                if (cut < 0 || run.First < cut) cut = run.First;
            }
        }

        return cut > first ? cut : -1;   // a cut at the very start is not a cut: the whole segment paints after
    }

    // A group's units map onto its run one-for-one only when the run accounts for all of them; a partly-culled group gives
    // its entries up rather than keep a guess, and the next walk restores them.
    private void ReslotUnits(ControlGroup group)
    {
        // TEXT keeps its own map, and it is a RANGE per unit rather than a slot: a block owns a whole glyph run. Leaving
        // it behind after a re-issue is what froze a live readout - the block went on patching the offsets it was
        // recorded at, which by then belonged to whatever had moved into them, so its own text simply stopped changing
        // until something forced a walk.
        if (ReferenceEquals(group.Arena, _textBatch))
        {
            ReslotTextRuns(group);
            return;
        }

        // Everything else keeps its slots in _sdfSlotByUnit, written by the walk; the map below is the RECT one.
        if (group.Arena != null && !ReferenceEquals(group.Arena, _rectBatch)) return;

        var total = 0;
        foreach (var run in group.Runs) total += run.Count;
        if (total != group.Units.Count || group.Runs.Count != 1)
        {
            foreach (var u in group.Units) _rectSlotByUnit.Remove(u);
            return;
        }

        var i = group.Runs[0].First;
        foreach (var u in group.Units) _rectSlotByUnit[u] = i++;
    }
}
