using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.Vulkan.Core;

namespace Adamantium.UI.Rendering;

// Base for CPU-baked instanced batches: an append-only CPU array mirrored into a BDA storage buffer, drawn as segments
// (runs sharing one scissor). Derived types bake items (TryAdd + MarkPending) and draw segments; RenderCache groups them.
internal abstract class BatchCollector<TItem> : BatchArena where TItem : struct
{
    // Size of ONE instance. Static readonly, so it is computed once per closed type - a Marshal.SizeOf per DRAW showed up
    // as microseconds a call in the replay breakdown, and a replayed frame issues dozens of them.
    protected static readonly int Stride = Marshal.SizeOf<TItem>();

    protected TItem[] Items;
    protected int Count;               // items written this frame (across all segments, monotonic within a frame)

    /// <summary>How far the RETAINED content reaches - the end of the furthest slot the segments still issue.
    /// <para>Not <see cref="Count"/>: that is the write cursor of THIS frame and is reset by every
    /// <see cref="BeginFrame"/>, so on a patch frame it names a dozen slots while the segments go on issuing thousands
    /// that were recorded frames ago. Anything asking "which instances are on screen" has to ask the segments.</para></summary>
    protected int RetainedExtent
    {
        get
        {
            var end = Count;
            foreach (var segment in _segments)
            {
                var last = (int)(segment.First + segment.Count);
                if (last > end) end = last;
            }

            return Math.Min(end, Items?.Length ?? 0);
        }
    }
    private int _segmentStart;         // start of the pending (not-yet-flushed) segment
    private Rect2D _scissor;           // the pending segment's clip
    private double _uL, _uT, _uR, _uB; // logical union of the pending segment (paint-order overlap test)
    private bool _hasUnion;

    // A ring of GPU copies, so a write never lands in a buffer that frames in flight are still reading.
    private Buffer<TItem>[] _ring;
    private int _current;              // ring slot this frame writes and draws from
    private uint _writeFrame = uint.MaxValue;   // device frame the slot was last chosen for
    private int _gpuCapacity;

    // Per copy: the bytes that copy currently holds, and how many of them are valid. The upload diffs the freshly baked
    // items against THIS copy (not against last frame), so only what that copy is actually missing is sent - a still
    // scene converges to zero bytes within a lap of the ring, and a scroll sends the span that moved.
    private TItem[][] _mirror;
    private int[] _mirrorCount;
    private int _highWater;   // see UploadRange: the furthest slot the array has ever been written at

    // A recorded segment (scissor + buffer range) replayed on clean frames. Capacity is its room, Count what it draws;
    // Id survives splits and is never reused; Bounds is its paint footprint, used to decide overlap for new placements.
    protected struct Segment
    {
        public int Id; public Rect2D Scissor; public uint Count; public uint First; public uint Capacity;
        public double L, T, R, B; public bool HasBounds;
    }
    private readonly List<Segment> _segments = new();
    private readonly Dictionary<int, int> _indexById = new();
    private int _nextSegmentId;

    /// <summary>Where a segment currently sits in draw order, or -1 if this id is not part of the recorded frame.</summary>
    private int IndexOf(int id) => _indexById.TryGetValue(id, out var index) ? index : -1;

    protected int SegmentIndexOf(int id) => IndexOf(id);

    /// <summary>Same, for a derived collector that keeps per-segment state of its own (the text sheet).</summary>
    protected int IndexOfSegment(int id) => IndexOf(id);

    /// <summary>The id of the segment sitting at this position in draw order - the way back from a position (which is
    /// what a slot search answers) to the NAME everything outside uses.</summary>
    public override int SegmentIdAt(int index) => index >= 0 && index < _segments.Count ? _segments[index].Id : -1;

    /// <summary>Is this id still part of the recorded frame?</summary>
    public override bool HasSegment(int id) => _indexById.ContainsKey(id);

    // Re-point the id map from that index to the end of the list. Called after an insert, which is the only thing that moves a
    // segment's index.
    private void Reindex(int from)
    {
        // Span: Segment is a STRUCT of ~80 bytes, so the list indexer copies the whole of it to read one int. This runs
        // over the segment tail after every insert.
        var segments = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_segments);
        for (var i = from; i < segments.Length; i++) _indexById[segments[i].Id] = i;
    }

    // Ranges vacated by a re-issued layer, handed back so the next re-issue reuses them instead of growing the arena.
    private readonly List<(int First, int Count)> _freeBlocks = new();

    protected BatchCollector(int initialCapacity) => Items = new TItem[initialCapacity];

    /// <summary>A pending (not-yet-flushed) segment exists.</summary>
    public bool Active => Count > _segmentStart;

    /// <summary>How many slots the retained arena currently holds, and what one of them holds. The cache sweeps them to
    /// find any whose owner has stopped drawing - the arena issues segments as RANGES, so such a slot is drawn by its
    /// neighbors' draw call.</summary>
    public int SlotCount => Count;

    public TItem ItemAt(int slot) => Items[slot];

    /// <summary>Absolute slot index of the item written by the LAST successful TryAdd (= its position in the retained
    /// buffer). RenderCache records it per unit during the walk so a partial-replay can address that unit's slot.</summary>
    public int LastSlot => Count - 1;

    /// <summary>GPU-buffer element capacity for THIS frame - derived TryAdd guards against overflowing it.</summary>
    protected int GpuCapacity => _gpuCapacity;

    /// <summary>The batch buffer - a BDA STORAGE buffer whose device address feeds the instanced shader.</summary>
    protected Buffer<TItem> GpuBuffer => _ring?[_current];

    /// <summary>Hand every GPU buffer this collector owns back to the device. Nothing else does: DisposeUnits frees the
    /// render UNITS, and a collector's ring - one host-visible buffer per frame in flight, per collector, and there are a
    /// dozen collectors per cache - stayed alive for the process. A closed window leaked all of it.</summary>
    public void DisposeGpuResources(IGraphicsDevice device)
    {
        OnDisposeGpuResources();
        if (_ring == null) return;

        foreach (var buffer in _ring)
        {
            if (buffer != null) device.AddToDeferDisposeQueue(buffer);
        }

        _ring = null;
        _mirror = null;
        _mirrorCount = null;
        _highWater = 0;
        _gpuCapacity = 0;
        _segments.Clear();
        _freeBlocks.Clear();
        Count = 0;
    }

    public void BeginFrame(IGraphicsDevice device)
    {
        // HEADROOM for patches. Capacity can only change here (the GPU buffer must not be reallocated under frames in
        // flight), so a walk that fits the scene exactly would leave a later patch nowhere to put a layer that GREW by one
        // item, and every such frame would fall back to the walk - which resets capacity to exactly the scene again.
        EnsureCpuCapacity(Count + Math.Max(64, Count / 8));

        Count = 0;
        _segmentStart = 0;
        _hasUnion = false;
        _segments.Clear();
        _indexById.Clear();
        _freeBlocks.Clear();
        EnsureRing(device);
        SelectSlot(device);
        OnBeginFrame(device);
    }

    // TEMP: ADAMANTIUM_NO_RING=1 collapses the ring to one copy, to check the write probe fires on a known violation.
    private static readonly bool RingDisabled = Environment.GetEnvironmentVariable("ADAMANTIUM_NO_RING") == "1";

    // Old buffers go to the deferred queue: frames in flight still read them.
    private void EnsureRing(IGraphicsDevice device)
    {
        var copies = RingDisabled ? 1 : (int)Math.Max(1, device.MaxFramesInFlight);
        if (_ring != null && _ring.Length == copies && _gpuCapacity >= Items.Length) return;

        if (_ring != null)
        {
            foreach (var buffer in _ring)
            {
                if (buffer != null) device.AddToDeferDisposeQueue(buffer);
            }
        }

        _ring = new Buffer<TItem>[copies];
        _mirror = new TItem[copies][];
        _mirrorCount = new int[copies];
        for (var i = 0; i < copies; i++)
        {
            _ring[i] = Adamantium.Graphics.Buffer.New<TItem>(device, (uint)Items.Length,
                BufferUsageFlags.StorageBuffer | BufferUsageFlags.ShaderDeviceAddress,
                MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.DeviceLocal);
            _mirror[i] = new TItem[Items.Length];
            _mirrorCount[i] = 0;   // a fresh buffer holds nothing -> everything differs -> first write sends it all
        }
        _gpuCapacity = Items.Length;
    }

    // Round-robin per write, not per frame: one walk's copy is read by several replay frames that follow it.
    private int _writeCursor;

    private void SelectSlot(IGraphicsDevice device)
    {
        _current = _writeCursor % _ring.Length;
        _writeCursor = (_writeCursor + 1) % _ring.Length;
        _writeFrame = device.CurrentFrame;
        if (_mirror[_current].Length < Items.Length) Array.Resize(ref _mirror[_current], Items.Length);
    }

    // A write that does NOT come from a walk (a partial patch replaying last frame's ops) still has to land in THIS
    // frame's copy, and that copy is a lap behind - bring it up to the retained data first, by diff, then patch it.
    protected void PrepareRetainedWrite(IGraphicsDevice device)
    {
        if (_ring == null || device.CurrentFrame == _writeFrame) return;
        SelectSlot(device);

        // Catch up to the high-water mark, not Count: slots past the cursor can still be issued and must match on every
        // copy.
        UploadRange(0, Math.Max(Count, _highWater));
    }

    // Sends [first, first+count) to the current copy, but only the one contiguous span that copy is actually missing.
    protected void UploadRange(int first, int count)
    {
        if (count <= 0) return;
        var mirror = _mirror[_current];
        var valid = _mirrorCount[_current];
        int lo = -1, hi = -1;
        for (var i = first; i < first + count; i++)
        {
            if (i < valid && SlotUnchanged(i, mirror)) continue;
            if (lo < 0) lo = i;
            hi = i;
        }
        if (lo >= 0)
        {
            _ring[_current].SetData(Items.AsSpan(lo, hi - lo + 1), (uint)(lo * Stride));
            Items.AsSpan(lo, hi - lo + 1).CopyTo(mirror.AsSpan(lo));
        }
        var end = first + count;
        if (end > _mirrorCount[_current]) _mirrorCount[_current] = end;

        // The furthest slot any write has ever reached. Maintained HERE because every write to the array passes through
        // this method, so it cannot drift from what is actually in there.
        if (end > _highWater) _highWater = end;
    }

    /// <summary>Per-frame hook for derived state (e.g. lazily creating the effect). Base does nothing.</summary>
    protected virtual void OnBeginFrame(IGraphicsDevice device) { }

    /// <summary>Frees what a derived collector built on the device - its effect - along with the buffers. Base does nothing.</summary>
    protected virtual void OnDisposeGpuResources() { }

    /// <summary>Whether anything is waiting to be flushed. The recorder needs it to know WHERE the next segment's paint
    /// span begins: a segment glues every control that falls between two flushes, so its span starts with the first one
    /// that put something in it - and the only moment that is knowable is while the segment is still empty.</summary>
    public bool HasPending => _hasUnion;

    /// <summary>The logical union of what is waiting to be flushed - the same four numbers <see cref="Flush"/> records on
    /// the segment. Read by the backdrop materials, which need the area their instances actually cover: a capture taken
    /// from the whole clip group spends most of its resolution on pixels no material fragment ever samples.</summary>
    public Rect PendingBounds => _hasUnion ? new Rect(_uL, _uT, _uR - _uL, _uB - _uT) : default;

    /// <summary>Does a unit's logical bounds overlap the pending segment? A later overlapping unit must draw AFTER a
    /// flush (painter's order); spatially disjoint units (a list's stacked items) don't.</summary>
    public bool OverlapsPending(Rect r)
        => _hasUnion && r.X < _uR && _uL < r.Right && r.Y < _uB && _uT < r.Bottom;

    /// <summary>Draw the pending segment (if any) and advance. Uploads ONLY this segment at its byte offset (earlier
    /// segments' GPU data stays intact for their recorded draws), sets its scissor, draws it via DrawSegment with a
    /// firstInstance offset, then restores fullScissor so the caller's per-unit scissor state stays valid.</summary>
    public int Flush(IGraphicsDevice device, Rect2D fullScissor, Matrix4x4F projection)
    {
        if (!Active) return -1;

        var segStart = _segmentStart;
        var count = Count - segStart;

        // Send this segment to THIS frame's copy - only the span that copy is missing. An unchanged scene converges to
        // zero bytes once every copy has seen it, which is what the old single-buffer "skip the upload when the scene is
        // clean" bought, without the assumption that last frame's bytes are still there to be reused.
        UploadRange(segStart, count);

        // Record the segment (clip + buffer range) and draw it. On a Clean frame the walk is skipped and RenderCache
        // replays this exact segment via DrawRecordedSegment (same code path, no upload) - so the immediate draw here
        // and the replayed draw are byte-for-byte the same.
        var index = _segments.Count;
        var id = ++_nextSegmentId;
        _segments.Add(new Segment
        {
            Id = id, Scissor = _scissor, Count = (uint)count, First = (uint)segStart, Capacity = (uint)count,
            L = _uL, T = _uT, R = _uR, B = _uB, HasBounds = _hasUnion
        });
        _indexById[id] = index;
        OnSegmentRecorded(index);

        _segmentStart = Count;
        _hasUnion = false;

        DrawRecordedSegment(device, id, fullScissor, projection);
        return id;
    }

    /// <summary>Draw a segment recorded this frame (by its <see cref="Flush"/> index): set its clip, bind any per-segment
    /// state, issue the instanced draw, restore <paramref name="fullScissor"/>. Called by the immediate draw in Flush AND
    /// by RenderCache's clean-frame op replay - the latter re-issues last frame's segments with zero re-bake/upload.</summary>
    public void DrawRecordedSegment(IGraphicsDevice device, int id, Rect2D fullScissor, Matrix4x4F projection)
    {
        var index = IndexOf(id);
        if (index < 0) return;   // not part of this recorded frame - draw nothing rather than draw somebody else

        var s = _segments[index];
        if (s.Count == 0) return;   // re-issued to nothing - nothing left to draw
        device.SetScissors(s.Scissor);
        BindSegment(index);
        DrawSegment(device, _ring[_current], s.Count, s.First, projection);
        device.SetScissors(fullScissor);
    }

    // --- Spliced-patch surgery (per-control render-cache patching) -------------------------------------------------
    // A control whose unit count changed gets a new segment instead of shifting slots; abandoned slots wait for the next
    // full walk to compact them.

    /// <summary>The recorded segment whose retained range contains <paramref name="slot"/>, or -1. Zero-count (fully
    /// excluded) segments never match.</summary>
    public override int FindSegmentContaining(int slot)
    {
        // Span + BY REFERENCE: the indexer copies an ~80-byte struct per iteration to test three fields.
        var segments = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_segments);
        for (var i = 0; i < segments.Length; i++)
        {
            ref var s = ref segments[i];
            if (s.Count > 0 && slot >= s.First && slot < s.First + s.Count) return s.Id;
        }
        return -1;
    }

    /// <summary>Is every slot of this run blank - written by nobody? Asked before a range is handed back, so a stale run
    /// cannot take a live neighbor with it. The base class cannot read an instance's fields, so the derived collector
    /// answers; a family with nothing to say answers "no" and simply never reclaims.</summary>
    protected virtual bool IsBlank(int first, int count) => false;

    /// <summary>Shrinks a segment's range past a departed control's run when the run is at its head or tail; a tail
    /// stays the segment's spare room.</summary>
    /// <returns>Whether the run left the drawn range.</returns>
    public bool ReclaimRun(int first, int count)
    {
        if (count <= 0) return false;

        var index = IndexOf(FindSegmentContaining(first));
        if (index < 0) return false;

        var s = _segments[index];
        var last = first + count;
        if (last > s.First + s.Count) return false;   // the run is not wholly inside what this segment draws

        // ...and it must be EMPTY - every slot in it already blanked. A group's runs can be stale: a walk that did not
        // visit the group reassigns its slots to whoever it recorded there, so the run may now name somebody else's
        // instances (which is why the blanking checks the owner tag before it writes). Shrinking a range by a stale
        // length takes a live neighbor out of the draw with it - measured as a card that vanished from the frame.
        if (!IsBlank(first, count)) return false;

        if (first == (int)s.First)
        {
            s.First += (uint)count;
            s.Count -= (uint)count;
            s.Capacity -= (uint)count;
            _segments[index] = s;
            _freeBlocks.Add((first, count));
            return true;
        }

        if (last == (int)(s.First + s.Count))
        {
            s.Count -= (uint)count;
            _segments[index] = s;   // the room past Count stays this segment's, to grow back into
            return true;
        }

        return false;
    }

    public override Rect2D GetSegmentScissor(int id)
    {
        var index = IndexOf(id);
        return index < 0 ? null : _segments[index].Scissor;
    }

    /// <summary>Replaces a recorded segment's scissor after its viewport moved (RenderCache.RefreshMovedScissors).</summary>
    public override void SetSegmentScissor(int id, Rect2D scissor)
    {
        var index = IndexOf(id);
        // Mutated IN PLACE: this read the struct, copied it, and wrote the copy back - two indexer passes over 80 bytes
        // to change one field.
        if (index >= 0) System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_segments)[index].Scissor = scissor;
    }

    /// <summary>What this recorded segment covers, in logical coordinates - empty when it never had bounds (a stale id, or
    /// a segment re-issued to nothing).</summary>
    public override Rect SegmentBounds(int id)
    {
        var index = IndexOf(id);
        if (index < 0) return Rect.Empty;

        var s = _segments[index];
        return s is { HasBounds: true, Count: > 0 } ? new Rect(s.L, s.T, s.R - s.L, s.B - s.T) : Rect.Empty;
    }

    /// <summary>Grow a recorded segment's footprint by what a patch has just put into it - a layer that gained an item now
    /// covers it, and the next placement has to see that.</summary>
    public override void GrowSegmentBounds(int id, Rect bounds)
    {
        var index = IndexOf(id);
        if (index < 0) return;

        var s = _segments[index];
        if (!s.HasBounds)
        {
            _segments[index] = s with { L = bounds.X, T = bounds.Y, R = bounds.Right, B = bounds.Bottom, HasBounds = true };
            return;
        }

        _segments[index] = s with
        {
            L = Math.Min(s.L, bounds.X), T = Math.Min(s.T, bounds.Y),
            R = Math.Max(s.R, bounds.Right), B = Math.Max(s.B, bounds.Bottom)
        };
    }

    /// TEMP (flicker hunt): what this recorded segment actually draws, for the walk-vs-replay trace comparison.
    public override string DescribeSegment(int id)
    {
        var index = IndexOf(id);
        if (index < 0) return $"seg#{id} MISSING (have {_segments.Count})";
        var s = _segments[index];
        var x = s.Scissor?.Offset?.X ?? -1;
        var y = s.Scissor?.Offset?.Y ?? -1;
        var w = s.Scissor?.Extent?.Width ?? 0;
        var h = s.Scissor?.Extent?.Height ?? 0;
        return $"first={s.First} count={s.Count} clip={x},{y} {w}x{h}";
    }

    /// <summary>Free retained capacity for patch appends this frame (capacity only grows at the next BeginFrame).</summary>
    public override int PatchCapacityLeft => _gpuCapacity - Count;

    /// <summary>Retained slot count (the next patch append starts here).</summary>
    public override int RetainedCount => Count;

    // Where a patch's freshly baked items wait between its validate phase and its mutate phase (see BatchArena). Typed,
    // so the bytes never leave this collector; the patch only ever names the range it appended.
    protected readonly List<TItem> Stage = new();

    public override void ClearStage() => Stage.Clear();

    public override int StagedCount => Stage.Count;

    /// <summary>A family that cannot bake a unit from a payload says so, and the patch refuses - which is what every
    /// family did before any of them could stage.</summary>
    public override bool TryStage(IRenderUnit unit, Matrix4x4F world, int transformSlot, int ownerTag, int clipSlot = -1) => false;

    public override bool ReplaceStagedInSegment(IGraphicsDevice device, int id, int at, int replaced, int stageFirst, int stageCount)
        => ReplaceInSegment(device, id, at, replaced, CollectionsMarshal.AsSpan(Stage).Slice(stageFirst, stageCount));

    public override bool RepointSegmentAroundStage(IGraphicsDevice device, int id, int first, int at, int replaced, int count,
        Rect2D scissor, int stageFirst, int stageCount)
    {
        _rebake.Clear();
        CopyRetained(first, at, _rebake);
        for (var i = 0; i < stageCount; i++) _rebake.Add(Stage[stageFirst + i]);
        CopyRetained(first + at + replaced, count - at - replaced, _rebake);
        return RepointSegment(device, id, CollectionsMarshal.AsSpan(_rebake), scissor);
    }

    public override void UpdateSlotFromStage(IGraphicsDevice device, int slot, int stageIndex)
        => UpdateSlot(device, slot, Stage[stageIndex]);

    public override int AllocateSegmentFromStage(IGraphicsDevice device, Rect2D scissor, int stageFirst, int stageCount)
        => AllocateSegment(device, CollectionsMarshal.AsSpan(Stage).Slice(stageFirst, stageCount), scissor);

    // Scratch for a relocating re-issue: head + staged + tail, assembled once and handed over as one span.
    private readonly List<TItem> _rebake = new();

    /// <summary>The retained range [first, first+count) a recorded segment currently draws.</summary>
    public override (int First, int Count) SegmentRange(int id)
    {
        var index = IndexOf(id);
        return index < 0 ? (-1, 0) : ((int)_segments[index].First, (int)_segments[index].Count);
    }

    /// <summary>Splits a recorded segment at <paramref name="firstOfSecond"/> without moving bytes, so a new control can be
    /// placed between them (RenderCache.PlaceNewSegment); returns the new segment's id.</summary>
    public override int SplitSegment(int id, int firstOfSecond)
    {
        var index = IndexOf(id);
        if (index < 0) return -1;

        var s = _segments[index];
        var offset = (uint)firstOfSecond - s.First;
        if (offset == 0 || offset >= s.Count) return -1;   // nothing on one side of the cut: not a split

        // The spare tail room goes to the second half; both halves keep the whole footprint, since per-item bounds are
        // not kept.
        var second = new Segment
        {
            Id = ++_nextSegmentId,
            Scissor = s.Scissor,
            First = (uint)firstOfSecond,
            Count = s.Count - offset,
            Capacity = s.Capacity - offset,
            L = s.L, T = s.T, R = s.R, B = s.B, HasBounds = s.HasBounds
        };
        _segments[index] = s with { Count = offset, Capacity = offset };
        _segments.Insert(index + 1, second);

        // Per-segment state (a texture, a field) is keyed by index too, and both halves carry the same one.
        OnSegmentInserted(index + 1);

        // The insert moved every later segment one along - which is why nobody outside holds an index. Fixed HERE, once.
        Reindex(index + 1);
        return second.Id;
    }

    /// <summary>Copy retained items out, for a caller re-issuing a segment: the instances of the groups that did NOT
    /// change are carried over as bytes rather than re-baked, so re-issuing a layer costs a copy, not a re-computation.</summary>
    public void CopyRetained(int first, int count, List<TItem> into)
    {
        for (var i = 0; i < count; i++) into.Add(Items[first + i]);
    }

    /// <summary>Replaces [at, at+replaced) inside a segment, shifting only what follows; false when the result outgrows
    /// the segment's room.</summary>
    public bool ReplaceInSegment(IGraphicsDevice device, int id, int at, int replaced, ReadOnlySpan<TItem> items)
    {
        var index = IndexOf(id);
        if (index < 0) return false;

        var s = _segments[index];
        var newCount = (int)s.Count - replaced + items.Length;
        if (newCount > (int)s.Capacity) return false;

        PrepareRetainedWrite(device);

        var first = (int)s.First;
        var delta = items.Length - replaced;
        var tailAt = first + at + replaced;
        var tailLen = (int)s.Count - at - replaced;
        if (delta != 0 && tailLen > 0) Array.Copy(Items, tailAt, Items, tailAt + delta, tailLen);
        items.CopyTo(Items.AsSpan(first + at));

        // A shrinking segment leaves stale copies in the vacated tail, which would be issued again when it grows back.
        if (delta < 0) Array.Clear(Items, first + newCount, -delta);

        var touched = items.Length + (delta != 0 ? tailLen + Math.Max(0, -delta) : 0);
        UploadRange(first + at, touched);
        _segments[index] = s with { Count = (uint)newCount };
        return true;
    }

    /// <summary>Register a NEW segment over freshly written items - for a control that starts drawing where nothing of its
    /// own was recorded. It gets its own range (reused from a vacated one where possible) and its own op, placed by paint
    /// rank; nothing already recorded moves. Returns the segment index, or -1 when the arena has no room.</summary>
    public int AllocateSegment(IGraphicsDevice device, ReadOnlySpan<TItem> items, Rect2D scissor)
    {
        PrepareRetainedWrite(device);

        var want = items.Length + Math.Max(16, items.Length / 8);
        var first = -1;
        for (var i = 0; i < _freeBlocks.Count; i++)
        {
            if (_freeBlocks[i].Count < want) continue;
            first = _freeBlocks[i].First;
            want = _freeBlocks[i].Count;
            _freeBlocks.RemoveAt(i);
            break;
        }

        if (first < 0)
        {
            if (want > PatchCapacityLeft) return -1;
            first = Count;
            EnsureCpuCapacity(first + want);
            Count = first + want;
        }

        items.CopyTo(Items.AsSpan(first));
        UploadRange(first, items.Length);
        var id = ++_nextSegmentId;
        var index = _segments.Count;
        _indexById[id] = index;
        _segments.Add(new Segment { Id = id, Scissor = scissor, Count = (uint)items.Length, First = (uint)first, Capacity = (uint)want });

        // A segment like any other, so per-segment state - a font sheet, a texture - is taken for it as a walk takes it.
        // Without it the next draw of this segment bound state it never had, and threw on every frame after.
        OnSegmentRecorded(index);
        return id;   // its footprint comes from the caller (GrowSegmentBounds): only it knows the logical bounds it baked
    }

    /// <summary>Hands a block back to the pool EMPTY. A freed block still holds its last tenant's instances - whole, with
    /// their owner tags - and the block is handed out again by capacity, not by how much the new tenant writes: whatever
    /// it does not cover keeps the previous occupant, ready to be issued the moment that segment grows over it. A
    /// scrollbar the window outgrew came back this way, at the size it had. Freeing means empty, on both sides.</summary>
    private void FreeBlock(IGraphicsDevice device, int first, int capacity)
    {
        if (capacity <= 0 || first < 0 || first + capacity > Items.Length) return;

        Array.Clear(Items, first, capacity);
        UploadRange(first, capacity);
        _freeBlocks.Add((first, capacity));
    }

    /// <summary>Re-issues a whole segment over new items, keeping its id and op-stream position; false when the arena has
    /// no room, and the caller falls back to a full walk.</summary>
    public bool RepointSegment(IGraphicsDevice device, int id, ReadOnlySpan<TItem> items, Rect2D scissor)
    {
        var index = IndexOf(id);
        if (index < 0) return false;

        PrepareRetainedWrite(device);

        var s = _segments[index];
        var need = items.Length;

        // Inside its own room: the normal case once a layer has been re-issued once, and the only one a hover ever needs.
        if (need <= (int)s.Capacity)
        {
            items.CopyTo(Items.AsSpan((int)s.First));

            // Same duplicate as the shrinking replace above: a segment that now holds FEWER items leaves the old ones in
            // the slots beyond its new end, whole and owned. They wait there until the segment grows back over them.
            var shrankBy = (int)s.Count - need;
            if (shrankBy > 0) Array.Clear(Items, (int)s.First + need, shrankBy);

            UploadRange((int)s.First, need + Math.Max(0, shrankBy));
            _segments[index] = s with { Scissor = scissor, Count = (uint)need };
            return true;
        }

        // Outgrew it: take a bigger block, WITH room to grow again. Handing out exactly what is asked for means the block
        // freed here (N) never fits the next request (N+1), and the arena fills with near-misses.
        var want = need + Math.Max(16, need / 8);
        var first = -1;
        for (var i = 0; i < _freeBlocks.Count; i++)
        {
            if (_freeBlocks[i].Count < want) continue;
            first = _freeBlocks[i].First;
            want = _freeBlocks[i].Count;   // take the block whole; splitting it leaves shards nothing fits in
            _freeBlocks.RemoveAt(i);
            break;
        }

        if (first < 0)
        {
            if (want > PatchCapacityLeft) return false;
            first = Count;
            EnsureCpuCapacity(first + want);
            Count = first + want;
        }

        if (s.Capacity > 0) FreeBlock(device, (int)s.First, (int)s.Capacity);

        items.CopyTo(Items.AsSpan(first));
        UploadRange(first, need);
        _segments[index] = s with { Scissor = scissor, Count = (uint)need, First = (uint)first, Capacity = (uint)want };
        return true;
    }

    /// <summary>Blanks a run's slots in place; the segment range still covers them until the next recording walk.</summary>
    public override void BlankSlots(IGraphicsDevice device, int first, int count)
    {
        if (first >= 0 && count > 0) BlankRun(device, (uint)first, (uint)count);
    }

    // Blanks without the Count clamp: BeginFrame resets Count but not the array, and stale bytes past it would be issued
    // again when segments grow back.
    protected void BlankRunPastTheCursor(IGraphicsDevice device, uint first, uint count)
    {
        if (count == 0 || first + count > (uint)(Items?.Length ?? 0)) return;

        PrepareRetainedWrite(device);
        for (var i = 0u; i < count; i++) Items[first + i] = default;
        UploadRange((int)first, (int)count);
    }

    public void BlankRun(IGraphicsDevice device, uint first, uint count)
    {
        if (count == 0 || first + count > (uint)Count) return;

        PrepareRetainedWrite(device);
        for (var i = 0u; i < count; i++) Items[first + i] = default;
        UploadRange((int)first, (int)count);
    }

    /// <summary>Overwrites one retained slot in place, so a partial frame can replay the recorded ops; call before
    /// the next BeginFrame.</summary>
    public virtual void UpdateSlot(IGraphicsDevice device, int slot, TItem item)
    {
        PrepareRetainedWrite(device);
        Items[slot] = item;
        UploadRange(slot, 1);
    }

    /// <summary>Hook: capture per-segment state at record time (the text batch stashes the segment's atlas). Base no-op.</summary>
    protected virtual void OnSegmentRecorded(int index) { }

    /// <summary>Hook: a segment was INSERTED at this index by a split, so index-keyed per-segment state has to make room
    /// for it - carrying a copy of what the segment being split held, since both halves draw the same way. Base no-op.</summary>
    protected virtual void OnSegmentInserted(int index) { }

    /// <summary>Hook: restore the per-segment state captured by <see cref="OnSegmentRecorded"/> before its draw. Base no-op.</summary>
    protected virtual void BindSegment(int index) { }

    // Does slot i already hold, in this copy, exactly what we baked? A blittable per-item bytewise compare (the items are
    // unmanaged Vector4F structs), SIMD-accelerated by SequenceEqual - cheap relative to the GPU upload it lets us skip.
    private bool SlotUnchanged(int i, TItem[] mirror)
        => MemoryMarshal.AsBytes(Items.AsSpan(i, 1)).SequenceEqual(MemoryMarshal.AsBytes(mirror.AsSpan(i, 1)));

    /// <summary>Emit the instanced draw for [firstInstance, firstInstance + count) of <paramref name="buffer"/>. The
    /// segment's scissor is already set; the derived type sets its own blend/depth/effect state + pass.</summary>
    protected abstract void DrawSegment(IGraphicsDevice device, Buffer<TItem> buffer, uint count, uint firstInstance, Matrix4x4F projection);

    /// <summary>Record the pending segment's clip + grow its overlap union. Derived TryAdd calls this AFTER writing its
    /// item(s) into Items/Count. All items of one segment share the clip (the caller enforces it).</summary>
    protected void MarkPending(Rect2D scissor, Rect logicalBounds)
    {
        _scissor = scissor;
        if (!_hasUnion) { _uL = logicalBounds.X; _uT = logicalBounds.Y; _uR = logicalBounds.Right; _uB = logicalBounds.Bottom; _hasUnion = true; return; }
        if (logicalBounds.X < _uL) _uL = logicalBounds.X;
        if (logicalBounds.Y < _uT) _uT = logicalBounds.Y;
        if (logicalBounds.Right > _uR) _uR = logicalBounds.Right;
        if (logicalBounds.Bottom > _uB) _uB = logicalBounds.Bottom;
    }

    /// <summary>Grow the CPU array to fit <paramref name="needed"/> items (also raises next frame's GPU size via
    /// BeginFrame). Derived TryAdd calls this BEFORE writing, then guards Count + n against <see cref="GpuCapacity"/>.</summary>
    protected void EnsureCpuCapacity(int needed)
    {
        if (needed <= Items.Length) return;
        var newLen = Items.Length;
        while (newLen < needed) newLen *= 2;
        Array.Resize(ref Items, newLen);
    }
}
