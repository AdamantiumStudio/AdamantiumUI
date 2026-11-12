using Adamantium.Graphics.Core.EffectsFramework;
﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Adamantium.Core;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Extensions;
using Adamantium.Graphics.Core.Models;
using Adamantium.Graphics.Core.Vertices;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.FX;
using Adamantium.UI.Rendering;
using Adamantium.UI.Rendering.RenderUnits;
using Adamantium.Vulkan.Core;
using Buffer = Adamantium.Graphics.Buffer;

namespace Adamantium.UI.Rendering.Retained;

// Instanced fills of tessellated meshes shared per GeometryKey, collected during the walk and flushed in paint order.
// Each flush draws one instanced call per key, then the deferred per-unit fringes and strokes on top. Render thread only.
internal sealed class InstancedFillCollector : DeferredDisposableObject
{
    // Master toggle for the general-geometry instancing path (off = solid arbitrary-geometry fills draw per-unit).
    public static bool Enabled = true;

    /// <summary>Whether a solid fill is drawn from its curves - exact at any zoom - rather than from its tessellated mesh
    /// and fringe.</summary>
    public static bool CurvesEnabled = true;

    /// <summary>Whether a curve fill's coverage comes from the flush's screen tiles, binned on the GPU (see
    /// <see cref="CurveTiler"/>), rather than from a walk over its every piece - what a flush the tiles cannot place
    /// takes anyway.</summary>
    public static bool CurveTilesEnabled = true;

    private const MemoryPropertyFlags Mem = MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.DeviceLocal;
    private static readonly int InstanceStride = Marshal.SizeOf<GeometryInstance>();
    private static readonly int GradInstanceStride = Marshal.SizeOf<GradientGeometryInstance>();
    private static readonly int PatInstanceStride = Marshal.SizeOf<PatternGeometryInstance>();
    private static readonly int TexInstanceStride = Marshal.SizeOf<TextureGeometryInstance>();

    // Per-key GPU + per-frame accumulation state. The mesh buffers are exact-size and written once (another mesh is
    // another key); the instance buffer is rewritten each frame (grown only at BeginFrame).
    private sealed class KeySegment
    {
        public Buffer VtxBuffer, IdxBuffer;
        public uint VertexCount, IndexCount;
        public bool Indexed;
        public PrimitiveType Topology;
        public bool MeshUploaded;

        // The shared analytic-AA ring for this key's mesh (built once with the mesh - see FrozenMesh.Ring). Every
        // instance draws it with the SAME instance buffer as the body, so N elements cost one fringe draw instead of N.
        public Buffer RingBuffer;
        public uint RingVertexCount;

        // A curve fill's packed curves (see CurveFill), in place of a mesh: drawn as one quad per instance.
        public Buffer CurveBuffer;

        // The same curves as the CPU keeps them: their box and counts, read when a flush bins them into tiles.
        public uint[] CurveWords;

        // When (Stopwatch ticks) this key was last asked for. A key is kept by being USED and by nothing else - see
        // Sweep for why the cache has to forget.
        public long LastUsed;

        // The device memory its vertex, index and fringe buffers hold - what the budget counts.
        public long MeshBytes;

        public GeometryInstance[] Items = new GeometryInstance[64];
        public int Count;        // instances appended this frame (across all this key's flushes)
        public int Flushed;      // instances already drawn this frame (= firstInstance for the next flush)
        // The instance data is rebuilt EVERY frame, so a single buffer was being overwritten while frames that drew from
        // it were still in flight (measured: hundreds of such writes per second of scrolling). One buffer per frame in
        // flight, picked by the device's frame slot - the same slot whose fence BeginDraw waits on. Gpu/GradGpu/PatGpu
        // stay as "the copy this frame writes and draws", so the collect + draw code below is unchanged.
        public Buffer[] GpuRing, GradGpuRing, PatGpuRing;
        public Buffer Gpu;       // instance SSBO (BDA); = GpuRing[frame slot]
        public int GpuCapacity;
        public bool Recreated;   // grown this frame -> a clean-skip is unsafe (fresh buffer holds no prior bytes)
        public bool InPending;   // currently listed in _pendingKeys

        // Parallel GRADIENT instance state for this key's shared mesh (a gradient fill on the same geometry). Solid and
        // gradient instances of one mesh share the vtx/idx buffers but have separate instance buffers + draw passes.
        public GradientGeometryInstance[] GradItems = new GradientGeometryInstance[16];
        public int GradCount;
        public int GradFlushed;
        public Buffer GradGpu;
        public int GradGpuCapacity;
        public bool GradRecreated;

        // Parallel PATTERN/NOISE instance state for this key's shared mesh (a procedural fill on the same geometry).
        public PatternGeometryInstance[] PatItems = new PatternGeometryInstance[16];
        public int PatCount;
        public int PatFlushed;
        public Buffer PatGpu;
        public int PatGpuCapacity;
        public bool PatRecreated;

        // Parallel TEXTURED instance state for this key's shared mesh (an ImageBrush fill on the same geometry). The
        // texture is NOT part of the key: one is bound per DRAW, so a texture change splits the run, not the segment.
        public TextureGeometryInstance[] TexItems = new TextureGeometryInstance[16];
        public int TexCount;
        public int TexFlushed;
        public Buffer[] TexGpuRing;
        public Buffer TexGpu;
        public int TexGpuCapacity;
        public bool TexRecreated;
        public ITexture PendingTexture;   // what the instances appended since the last run all sample
        public SamplerState PendingSampler;   // and how - one picture's, as the texture is

        // Parallel MATERIAL instance state for this key's shared mesh. The record is the PATTERN one deliberately: both
        // feed PatternGeomData, so a second identical struct would only be a second thing to keep in step.
        public PatternGeometryInstance[] MatItems = new PatternGeometryInstance[16];
        public int MatCount;
        public int MatFlushed;
        public Buffer[] MatGpuRing;
        public Buffer MatGpu;
        public int MatGpuCapacity;
        public bool MatRecreated;
        // What the instances appended since the last run agree about: the image bound to them, the pass that reads it,
        // and the frame region the capture is taken from. A disagreement about either of the first two ends the run.
        public bool MatPending;
        public bool MatWallpaper;
        public Core.Media.MaterialTreatment MatTreatment;
        public Rect2D MatRegion;
        public ITexture MatSource;          // a picture of the author's own, replacing the built-in source
        public MaterialAnchor MatAnchor;    // and what it is pinned to, which decides the mapping
    }

    private readonly IGraphicsDevice _device;
    // BOTH effects, because this collector draws both families: a solid instanced fill and its fringe come from
    // BatchEffect, while the gradient/pattern/texture fills of the SAME shared mesh come from BrushEffect. The shared
    // per-frame parameters (projection, transforms, viewport, fringe width) are therefore written into BOTH - the price
    // of the split, paid once per state setup rather than per draw.
    private readonly BatchEffect _effect;
    private BrushEffect _brush;
    private readonly Dictionary<GeometryKey, KeySegment> _keys = new();

    // Pending (not-yet-flushed) clip group: the keys with unflushed instances + the units whose fringe/stroke draw AFTER
    // the fills, plus the shared clip and a logical union for the paint-order overlap test.
    private readonly List<KeySegment> _pendingKeys = new();
    private readonly List<IRenderUnit> _pendingUnits = new();
    private Rect2D _scissor;
    private double _uL, _uT, _uR, _uB;
    private bool _hasUnion;

    // One clip group's recorded flush (key ranges, deferred units, clip), replayed by ReplayFlush on clean frames; pooled.
    private sealed class FlushRecord
    {
        public readonly List<(KeySegment Seg, uint First, uint Count)> Keys = new();
        public readonly List<(KeySegment Seg, uint First, uint Count)> GradKeys = new();
        // The pattern/noise runs, each UNIFORM IN KIND: every kind is its own pass now, so a run is cut wherever the kind
        // changes (see RecordGroup). Without that a single draw would paint two different fields with one shader.
        public readonly List<(KeySegment Seg, uint First, uint Count, int Kind)> PatKeys = new();
        // Textured runs carry their TEXTURE: one is bound per draw, so a run ends where the texture changes.
        public readonly List<(KeySegment Seg, uint First, uint Count, ITexture Texture, SamplerState Sampler)> TexKeys = new();
        // Material runs carry the whole description of their source: which image (a capture of the frame, or the
        // desktop), which pass reads it, and which region the capture is taken from.
        public readonly List<(KeySegment Seg, uint First, uint Count, bool Wallpaper, Core.Media.MaterialTreatment Treatment,
            Rect2D Region, ITexture Source, MaterialAnchor Anchor)> MatKeys = new();
        public readonly List<IRenderUnit> Units = new();
        public Rect2D Scissor;

        // First stencil mark of this group; key N stamps StencilRef + N in paint order, and fringes draw only where the
        // stencil is lower. Replays reuse the recorded value.
        public uint StencilRef;

        /// <summary>The mark key <paramref name="index"/> stamps - its place in the group's paint order, clamped to the
        /// field so a long group cannot wrap back onto an earlier key's mark.</summary>
        public uint MarkFor(int index) => Math.Min(StencilRef + (uint)index, CoverageMarkBits);

        // Its solid curve fills binned into screen tiles, kept with it so a replay draws from them without binning.
        public CurveTiler Tiles;

        public void Reset() { Keys.Clear(); GradKeys.Clear(); PatKeys.Clear(); TexKeys.Clear(); MatKeys.Clear(); Units.Clear(); }
    }
    private readonly List<FlushRecord> _flushRecords = new();
    private int _flushCount;   // records used this frame (pooled objects reused up to this count)

    private const uint CoverageMarkBits = 0xFF;

    // Marks are 8 bits and the buffer is cleared per frame, so a frame has 255 of them; past that the caller falls back
    // to closing the group on overlap.
    private uint _groupRef;

    /// <summary>True once this frame has used up the 255 distinct coverage marks.</summary>
    public bool CoverageMarksExhausted => _groupRef >= CoverageMarkBits;

    /// <summary>Set by the caller each frame: true when the scene is provably unchanged (RenderBuildKind.Clean) so the
    /// per-key instance upload is skipped (the retained buffer already holds these exact bytes).</summary>
    public bool SceneClean { get; set; }

    /// <summary>A collector drawing on <paramref name="device"/>; <paramref name="askForFrame"/> gets a frame drawn when
    /// meshes left behind may have gone cold while nothing changed, so they can be let go.</summary>
    public InstancedFillCollector(IGraphicsDevice device, Action askForFrame = null) : base(device)
    {
        _device = device;
        _effect = ToDispose(new BatchEffect(device));
        _meshBudget = MeshBudgetOf(device);
        var ask = askForFrame ?? Core.LoopSignal.Request;
        _wake = new Timer(_ => ask());
    }

    // What the shared meshes may hold before the ones drawn longest ago give way: a share of the largest device-local
    // heap, small on purpose - the rest of the application and other applications draw from the same heap.
    private static long MeshBudgetOf(IGraphicsDevice device)
    {
        var memory = device.Adapter.Adapter.GetPhysicalDeviceMemoryProperties();
        ulong largest = 0;
        for (var heap = 0; heap < memory.MemoryHeapCount; heap++)
        {
            var properties = memory.MemoryHeaps.Span[heap];
            if (((MemoryHeapFlagBits)properties.Flags).HasFlag(MemoryHeapFlagBits.DeviceLocalBit))
            {
                largest = Math.Max(largest, (ulong)properties.Size);
            }
        }

        return (long)(largest / MeshBudgetShare);
    }

    // The brush effect is built on FIRST BRUSH DRAW, not in the constructor: a tree of solid fills never needs it, and an
    // effect costs a set of shader objects per device. Building it unconditionally was enough extra device pressure to
    // crash the off-screen test host natively partway through a run. Returns it ready to use, with the per-frame state
    // this collector last set up already applied.
    private BrushEffect Brush(Matrix4x4F projection)
    {
        if (_brush != null) return _brush;

        _brush = ToDispose(new BrushEffect(_device));
        _brush.Projection.SetValue(projection);
        _brush.Transforms.SetValue(TransformsAddress);
        var vp = _device.CurrentViewports;
        if (vp is { Length: > 0 }) _brush.ViewportSize.SetValue(new Vector2F(vp[0].Width, vp[0].Height));
        _brush.TargetOrigin.SetValue(TargetOrigin());
        _brush.FringePixels.SetValue(DeviceFringePx);
        return _brush;
    }

    private Vector2F TargetOrigin() => new(_device.TargetOrigin.X, _device.TargetOrigin.Y);

    private Adamantium.UI.FX.MaterialEffect _material;

    private Adamantium.UI.FX.MaterialEffect Material(Matrix4x4F projection)
    {
        if (_material != null) return _material;

        _material = ToDispose(new Adamantium.UI.FX.MaterialEffect(_device));
        _material.Projection.SetValue(projection);
        _material.Transforms.SetValue(TransformsAddress);
        var vp = _device.CurrentViewports;
        if (vp is { Length: > 0 }) _material.ViewportSize.SetValue(new Vector2F(vp[0].Width, vp[0].Height));
        _material.TargetOrigin.SetValue(TargetOrigin());
        return _material;
    }

    /// <summary>Where a material instance's backdrop comes from. Handed over by the cache rather than built here,
    /// because there must be exactly ONE capture of the frame per region per frame - see
    /// <see cref="MaterialRectCollector.BindSource"/>.</summary>
    public MaterialRectCollector Backdrop { get; set; }

    /// <summary>A pending (not-yet-flushed) clip group exists.</summary>
    public bool Active => _pendingKeys.Count > 0;

    // --- test observability -------------------------------------------------------------------------------------------
    // The whole point of this collector is that identical geometry COLLAPSES into one shared mesh + one instanced draw,
    // and that a full instance buffer falls back per-unit instead of dropping a shape. Neither is visible from the public
    // surface (both live in the per-key segments), so expose just enough to assert them.
    internal int SegmentCount => _keys.Count;
    internal int PendingKeyCount => _pendingKeys.Count;
    internal int InstanceCountOf(GeometryKey key) => _keys.TryGetValue(key, out var seg) ? seg.Count : 0;
    internal int GpuCapacityOf(GeometryKey key) => _keys.TryGetValue(key, out var seg) ? seg.GpuCapacity : 0;

    /// <summary>Reset per-frame accumulation. Grows each key's instance buffer here (the safe point: the render runs after
    /// the frame fence, so last frame's GPU reads are done); a new key allocates lazily on its first add.</summary>
    public void BeginFrame()
    {
        // FIRST, while the flush records still hold what the last frame drew - those keys are on screen and stay - and
        // before a key about to go gets an instance ring made for it below.
        // Past the budget only once more has come since the last look: when what is on screen alone is past it, nothing
        // can go, and looking again every frame would only cost.
        var now = Clock();
        if (now >= _sweepAt || _meshBytes > _meshBudget && _meshBytes > _bytesAtSweep)
        {
            Sweep(now);
        }

        // Same rule as BatchCollector: advance per WRITE, not per frame index. These buffers are read by every replay
        // frame that follows the walk that filled them, so returning to a copy by frame index overwrites one that
        // in-flight replays are still drawing from.
        var copies = (int)Math.Max(1u, _device.MaxFramesInFlight);
        if (_device.FrameTicket != _copyFrame)
        {
            TakeNextCopy(copies);
        }

        var slot = _copy % copies;

        foreach (var seg in _keys.Values)
        {
            if (seg.GpuRing != null && (seg.GpuRing.Length != copies || seg.GpuCapacity < seg.Items.Length))
            {
                DeferRing(seg.GpuRing);
                seg.GpuRing = null;
            }
            if (seg.GpuRing == null && seg.MeshUploaded)
            {
                seg.GpuRing = new Buffer[copies];
                for (var i = 0; i < copies; i++)
                {
                    seg.GpuRing[i] = Buffer.New<GeometryInstance>(_device, (uint)seg.Items.Length,
                        BufferUsageFlags.StorageBuffer | BufferUsageFlags.ShaderDeviceAddress, Mem);
                }
                seg.GpuCapacity = seg.Items.Length;
                seg.Recreated = true;
            }
            // The clean-skip below asks "does this buffer already hold the bytes?", and with a ring the answer is no: the
            // copy this walk moves to was filled a lap ago and its contents belong to an older frame. Treat every copy
            // change as a fresh buffer, or a scene that reports itself unchanged draws from an unwritten copy - which is
            // exactly how the dropdown chevrons vanished until the pointer moved.
            else seg.Recreated = true;
            seg.Gpu = seg.GpuRing == null ? null : seg.GpuRing[slot];

            // Parallel grow/reset for the gradient instance buffer (only when this key has ever held gradient instances).
            if (seg.GradGpuRing != null && (seg.GradGpuRing.Length != copies || seg.GradGpuCapacity < seg.GradItems.Length))
            {
                DeferRing(seg.GradGpuRing);
                seg.GradGpuRing = null;
            }
            if (seg.GradGpuRing == null && seg.MeshUploaded && seg.GradCount > 0)
            {
                seg.GradGpuRing = new Buffer[copies];
                for (var i = 0; i < copies; i++)
                {
                    seg.GradGpuRing[i] = Buffer.New<GradientGeometryInstance>(_device, (uint)seg.GradItems.Length,
                        BufferUsageFlags.StorageBuffer | BufferUsageFlags.ShaderDeviceAddress, Mem);
                }
                seg.GradGpuCapacity = seg.GradItems.Length;
                seg.GradRecreated = true;
            }
            else seg.GradRecreated = true;
            seg.GradGpu = seg.GradGpuRing == null ? null : seg.GradGpuRing[slot];

            // Parallel grow/reset for the pattern instance buffer (only when this key has ever held pattern instances).
            if (seg.PatGpuRing != null && (seg.PatGpuRing.Length != copies || seg.PatGpuCapacity < seg.PatItems.Length))
            {
                DeferRing(seg.PatGpuRing);
                seg.PatGpuRing = null;
            }
            if (seg.PatGpuRing == null && seg.MeshUploaded && seg.PatCount > 0)
            {
                seg.PatGpuRing = new Buffer[copies];
                for (var i = 0; i < copies; i++)
                {
                    seg.PatGpuRing[i] = Buffer.New<PatternGeometryInstance>(_device, (uint)seg.PatItems.Length,
                        BufferUsageFlags.StorageBuffer | BufferUsageFlags.ShaderDeviceAddress, Mem);
                }
                seg.PatGpuCapacity = seg.PatItems.Length;
                seg.PatRecreated = true;
            }
            else seg.PatRecreated = true;
            seg.PatGpu = seg.PatGpuRing == null ? null : seg.PatGpuRing[slot];

            // Parallel grow/reset for the textured instance buffer (only when this key has ever held textured instances).
            if (seg.TexGpuRing != null && (seg.TexGpuRing.Length != copies || seg.TexGpuCapacity < seg.TexItems.Length))
            {
                DeferRing(seg.TexGpuRing);
                seg.TexGpuRing = null;
            }
            if (seg.TexGpuRing == null && seg.MeshUploaded && seg.TexCount > 0)
            {
                seg.TexGpuRing = new Buffer[copies];
                for (var i = 0; i < copies; i++)
                {
                    seg.TexGpuRing[i] = Buffer.New<TextureGeometryInstance>(_device, (uint)seg.TexItems.Length,
                        BufferUsageFlags.StorageBuffer | BufferUsageFlags.ShaderDeviceAddress, Mem);
                }
                seg.TexGpuCapacity = seg.TexItems.Length;
                seg.TexRecreated = true;
            }
            else seg.TexRecreated = true;
            seg.TexGpu = seg.TexGpuRing == null ? null : seg.TexGpuRing[slot];

            // Parallel grow/reset for the material instance buffer (only when this key has ever held material instances).
            if (seg.MatGpuRing != null && (seg.MatGpuRing.Length != copies || seg.MatGpuCapacity < seg.MatItems.Length))
            {
                DeferRing(seg.MatGpuRing);
                seg.MatGpuRing = null;
            }
            if (seg.MatGpuRing == null && seg.MeshUploaded && seg.MatCount > 0)
            {
                seg.MatGpuRing = new Buffer[copies];
                for (var i = 0; i < copies; i++)
                {
                    seg.MatGpuRing[i] = Buffer.New<PatternGeometryInstance>(_device, (uint)seg.MatItems.Length,
                        BufferUsageFlags.StorageBuffer | BufferUsageFlags.ShaderDeviceAddress, Mem);
                }
                seg.MatGpuCapacity = seg.MatItems.Length;
                seg.MatRecreated = true;
            }
            else seg.MatRecreated = true;
            seg.MatGpu = seg.MatGpuRing == null ? null : seg.MatGpuRing[slot];

            seg.Count = 0;
            seg.Flushed = 0;
            seg.GradCount = 0;
            seg.GradFlushed = 0;
            seg.PatCount = 0;
            seg.PatFlushed = 0;
            seg.TexCount = 0;
            seg.TexFlushed = 0;
            seg.PendingTexture = null;
            seg.MatCount = 0;
            seg.MatFlushed = 0;
            seg.MatPending = false;
            seg.InPending = false;
        }
        _pendingKeys.Clear();
        _pendingUnits.Clear();
        _hasUnion = false;
        _hasFringeUnion = false;
        foreach (var record in _flushRecords)
        {
            record.Reset();
        }

        _flushCount = 0;   // pooled flush records reused from index 0 this frame
        _groupRef = 0;     // the stencil is cleared with the frame, so coverage marks start over with it
    }

    /// <summary>Lets go of the meshes nobody has drawn for a while between walks too: a scene that stopped changing stops
    /// walking, and what it left behind was otherwise held until something moved. Render thread, any frame.</summary>
    public void Trim()
    {
        var now = Clock();
        if (!_disposed && now >= _sweepAt)
        {
            Sweep(now);
        }
    }

    private int _writeCursor;   // which ring copy the next frame that writes takes (see BeginFrame)
    private int _copy;          // the ring copy this frame writes and draws from
    private ulong _copyFrame = ulong.MaxValue;   // the device frame (its FrameTicket) that took it

    private void TakeNextCopy(int copies)
    {
        _copy = _writeCursor % copies;
        _writeCursor = (_writeCursor + 1) % copies;
        _copyFrame = _device.FrameTicket;
    }

    // A patch has to land in a copy no frame in flight reads: the first write of a frame moves every key to the next copy
    // and fills it with what the keys retain, then the patch goes on top. Same rule as BatchCollector.PrepareRetainedWrite.
    private void PrepareRetainedWrite()
    {
        if (_device.FrameTicket == _copyFrame) return;

        TakeNextCopy((int)Math.Max(1u, _device.MaxFramesInFlight));
        foreach (var seg in _keys.Values)
        {
            seg.Gpu = Retake(seg.GpuRing, seg.Gpu, seg.Items, seg.Count);
            seg.GradGpu = Retake(seg.GradGpuRing, seg.GradGpu, seg.GradItems, seg.GradCount);
            seg.PatGpu = Retake(seg.PatGpuRing, seg.PatGpu, seg.PatItems, seg.PatCount);
            seg.TexGpu = Retake(seg.TexGpuRing, seg.TexGpu, seg.TexItems, seg.TexCount);
            seg.MatGpu = Retake(seg.MatGpuRing, seg.MatGpu, seg.MatItems, seg.MatCount);
        }
    }

    private Buffer Retake<T>(Buffer[] ring, Buffer current, T[] items, int count) where T : struct
    {
        if (ring == null || _copy >= ring.Length) return current;

        var next = ring[_copy];
        if (count > 0) next.SetData(new ReadOnlySpan<T>(items, 0, count));
        return next;
    }

    internal int CurrentCopy => _copy;

    // How long a key nobody draws is kept, and how often the cache is looked over - in TIME, since frames are drawn only
    // when something changes and a still scene draws none. Generous: forgetting a mesh costs re-tessellating it once, so
    // a short memory buys nothing but a stutter when something comes back into view. The budget, not the age, is what
    // holds a map zoomed across levels - thousands of meshes a minute - in check.
    private static readonly long ColdTicks = Stopwatch.Frequency * 10;
    private static readonly long SweepTicks = Stopwatch.Frequency * 2;
    private const ulong MeshBudgetShare = 16;

    private long _meshBudget;
    private readonly Timer _wake;
    private long _meshBytes;
    private long _bytesAtSweep;
    private bool _disposed;
    private long _sweepAt;
    private readonly HashSet<KeySegment> _live = new();
    private readonly List<GeometryKey> _cold = new();
    private readonly List<KeyValuePair<GeometryKey, KeySegment>> _byAge = new();

    /// <summary>How many shared meshes the cache is holding - what <see cref="Sweep"/> keeps from growing without end.
    /// </summary>
    internal int CachedKeys => _keys.Count;

    /// <summary>The bytes the shared meshes hold now.</summary>
    internal long MeshBytes => _meshBytes;

    /// <summary>The bytes past which the meshes drawn longest ago give way; a share of the device's memory unless set.</summary>
    internal long MeshBudget
    {
        get => _meshBudget;
        set => _meshBudget = value;
    }

    /// <summary>Where the time a key was last asked for is read from, in <see cref="Stopwatch"/> ticks.</summary>
    internal Func<long> Clock { get; set; } = Stopwatch.GetTimestamp;

    // Drops keys nobody has asked for in a while (changing meshes leave a new key each time), then, past the budget, the
    // ones asked for longest ago - never one a flush record still replays. Keys left that may go cold later wake the
    // loop for a frame then, so a still scene lets go of them too.
    private void Sweep(long now)
    {
        _sweepAt = now + SweepTicks;
        _live.Clear();
        foreach (var record in _flushRecords)
        {
            foreach (var run in record.Keys) _live.Add(run.Seg);
            foreach (var run in record.GradKeys) _live.Add(run.Seg);
            foreach (var run in record.PatKeys) _live.Add(run.Seg);
            foreach (var run in record.TexKeys) _live.Add(run.Seg);
            foreach (var run in record.MatKeys) _live.Add(run.Seg);
        }

        _cold.Clear();
        _byAge.Clear();
        foreach (var pair in _keys)
        {
            if (_live.Contains(pair.Value)) continue;

            if (now - pair.Value.LastUsed >= ColdTicks) _cold.Add(pair.Key);
            else _byAge.Add(pair);
        }

        foreach (var key in _cold) Forget(key);

        if (_meshBytes > _meshBudget)
        {
            _byAge.Sort(static (a, b) => a.Value.LastUsed.CompareTo(b.Value.LastUsed));
            var kept = 0;
            for (; kept < _byAge.Count && _meshBytes > _meshBudget; kept++) Forget(_byAge[kept].Key);
            _byAge.RemoveRange(0, kept);
        }

        _bytesAtSweep = _meshBytes;
        if (_byAge.Count > 0 && !_disposed)
        {
            _wake.Change(TimeSpan.FromTicks(ColdTicks * TimeSpan.TicksPerSecond / Stopwatch.Frequency), Timeout.InfiniteTimeSpan);
        }

        _live.Clear();
        _cold.Clear();
        _byAge.Clear();
    }

    private void Forget(GeometryKey key)
    {
        if (!_keys.Remove(key, out var seg)) return;

        _arenas.Remove(key);   // its arena speaks for this segment; the key coming back makes both anew
        Release(seg);
    }

    // Everything a key owns, through the DEFERRED queue: frames in flight may still be reading any of it.
    private void Release(KeySegment seg)
    {
        if (seg.GpuRing != null) DeferRing(seg.GpuRing);
        if (seg.GradGpuRing != null) DeferRing(seg.GradGpuRing);
        if (seg.PatGpuRing != null) DeferRing(seg.PatGpuRing);
        if (seg.TexGpuRing != null) DeferRing(seg.TexGpuRing);
        if (seg.MatGpuRing != null) DeferRing(seg.MatGpuRing);

        seg.GpuRing = seg.GradGpuRing = seg.PatGpuRing = seg.TexGpuRing = seg.MatGpuRing = null;
        seg.Gpu = seg.GradGpu = seg.PatGpu = seg.TexGpu = seg.MatGpu = null;
        seg.GpuCapacity = seg.GradGpuCapacity = seg.PatGpuCapacity = seg.TexGpuCapacity = seg.MatGpuCapacity = 0;

        if (seg.VtxBuffer != null) _device.AddToDeferDisposeQueue(seg.VtxBuffer);
        if (seg.IdxBuffer != null) _device.AddToDeferDisposeQueue(seg.IdxBuffer);
        if (seg.RingBuffer != null) _device.AddToDeferDisposeQueue(seg.RingBuffer);
        if (seg.CurveBuffer != null) _device.AddToDeferDisposeQueue(seg.CurveBuffer);

        seg.VtxBuffer = null;
        seg.IdxBuffer = null;
        seg.RingBuffer = null;
        seg.CurveBuffer = null;
        seg.CurveWords = null;
        seg.MeshUploaded = false;
        _meshBytes -= seg.MeshBytes;
        seg.MeshBytes = 0;
    }

    protected override void Dispose(bool disposeManagedResources)
    {
        if (disposeManagedResources)
        {
            _disposed = true;
            _wake.Dispose();
            foreach (var seg in _keys.Values) Release(seg);
            _keys.Clear();
            foreach (var rec in _flushRecords) rec.Tiles?.Dispose();
            _arenas.Clear();
        }

        base.Dispose(disposeManagedResources);
    }

    // An outgoing ring may still be read by frames in flight - hand it to the device's deferred queue, never Dispose here.
    private void DeferRing(Buffer[] ring)
    {
        foreach (var buffer in ring)
        {
            if (buffer != null) _device.AddToDeferDisposeQueue(buffer);
        }
    }

    /// <summary>True if this unit's fill can join the instanced batch (solid arbitrary geometry with a drawable mesh).</summary>
    public bool CanBatch(GeometryRenderUnit unit) => unit.TryGetInstancedFill(out _, out _, out _);

    /// <summary>Collect one instanceable fill: append its per-instance world+color to its key's buffer and register the
    /// unit for a deferred fringe/stroke draw. False only if it can't be batched (no drawable mesh, or the instance buffer
    /// overflowed this frame) - the caller then draws that unit per-unit (fill included).</summary>
    public bool TryAdd(GeometryRenderUnit unit, Matrix4x4F local, Rect2D scissor, Rect logicalBounds, int transformSlot,
        int clipSlot = -1)
    {
        if (!unit.TryGetInstancedFill(out var key, out var meshObj, out var color)) return false;
        // The unit's OWN placement on top of the caller's bake (a Drawing's shape sitting at its own spot and scale
        // inside one element). Folded HERE rather than at the four call sites so this path and the per-unit one compose
        // it exactly once and the same way.
        local = unit.PlaceBaked(local);
        if (meshObj is not FrozenMesh mesh) return false;
        var seg = GetOrCreate(key, mesh);
        if (seg == null) return false;

        if (seg.Count + 1 > seg.Items.Length) Array.Resize(ref seg.Items, seg.Items.Length * 2);
        if (seg.Gpu == null)
        {
            // First add for this key (its mesh only just uploaded): build the WHOLE ring, not one buffer, or this key
            // would spend its life writing a single copy under the frames drawing it.
            var copies = (int)Math.Max(1u, _device.MaxFramesInFlight);
            if (seg.GpuRing != null) DeferRing(seg.GpuRing);
            seg.GpuRing = new Buffer[copies];
            for (var i = 0; i < copies; i++)
            {
                seg.GpuRing[i] = Buffer.New<GeometryInstance>(_device, (uint)seg.Items.Length,
                    BufferUsageFlags.StorageBuffer | BufferUsageFlags.ShaderDeviceAddress, Mem);
            }
            seg.Gpu = seg.GpuRing[_copy % copies];
            seg.GpuCapacity = seg.Items.Length;
            seg.Recreated = true;
        }
        if (seg.Count + 1 > seg.GpuCapacity) return false;   // this frame's GPU buffer is full -> per-unit fallback

        LastArena = _arenas.TryGetValue(key, out var known) ? known : _arenas[key] = new InstancedKeyArena(this, seg);
        LastSlot = seg.Count;   // ...and which slot of that arena it is, so the walk can note the group run
        seg.Items[seg.Count++] = GeometryInstance.FromLocal(local, color, transformSlot, FadeSlotFor(unit), clipSlot);

        _scissor = scissor;
        if (!seg.InPending) { seg.InPending = true; _pendingKeys.Add(seg); }
        AddPendingUnit(unit);
        NoteFringed(unit, logicalBounds);
        if (!_hasUnion) { _uL = logicalBounds.X; _uT = logicalBounds.Y; _uR = logicalBounds.Right; _uB = logicalBounds.Bottom; _hasUnion = true; }
        else
        {
            if (logicalBounds.X < _uL) _uL = logicalBounds.X;
            if (logicalBounds.Y < _uT) _uT = logicalBounds.Y;
            if (logicalBounds.Right > _uR) _uR = logicalBounds.Right;
            if (logicalBounds.Bottom > _uB) _uB = logicalBounds.Bottom;
        }
        return true;
    }

    /// <summary>True if this unit's fill can join the GRADIENT instanced batch (arbitrary geometry with a gradient fill).</summary>
    public bool CanBatchGradient(GeometryRenderUnit unit) => unit.TryGetInstancedGradientFill(out _, out _, out _, out _, out _);

    /// <summary>Collect one instanceable GRADIENT fill: append its per-instance world + gradient to its key's gradient
    /// buffer, and register the unit for a deferred fringe/stroke draw. False if it can't be batched (no drawable mesh or
    /// buffer overflow) - the caller draws it per-unit.</summary>
    public bool TryAddGradient(GeometryRenderUnit unit, Matrix4x4F local, Rect2D scissor, Rect logicalBounds, int transformSlot, int clipSlot = -1)
    {
        if (!unit.TryGetInstancedGradientFill(out var key, out var meshObj, out var brush, out var localBounds, out var opacity)) return false;
        local = unit.PlaceBaked(local);
        if (meshObj is not FrozenMesh mesh) return false;
        var seg = GetOrCreate(key, mesh);
        if (seg == null) return false;

        if (seg.GradCount + 1 > seg.GradItems.Length) Array.Resize(ref seg.GradItems, seg.GradItems.Length * 2);
        if (seg.GradGpu == null)
        {
            var copies = (int)Math.Max(1u, _device.MaxFramesInFlight);
            if (seg.GradGpuRing != null) DeferRing(seg.GradGpuRing);
            seg.GradGpuRing = new Buffer[copies];
            for (var i = 0; i < copies; i++)
            {
                seg.GradGpuRing[i] = Buffer.New<GradientGeometryInstance>(_device, (uint)seg.GradItems.Length,
                    BufferUsageFlags.StorageBuffer | BufferUsageFlags.ShaderDeviceAddress, Mem);
            }
            seg.GradGpu = seg.GradGpuRing[_copy % copies];
            seg.GradGpuCapacity = seg.GradItems.Length;
            seg.GradRecreated = true;
        }
        if (seg.GradCount + 1 > seg.GradGpuCapacity) return false;

        seg.GradItems[seg.GradCount++] = BuildGradientInstance(brush, local, localBounds, opacity, transformSlot, FadeSlotFor(unit), clipSlot);

        _scissor = scissor;
        if (!seg.InPending) { seg.InPending = true; _pendingKeys.Add(seg); }
        AddPendingUnit(unit);
        NoteFringed(unit, logicalBounds);
        if (!_hasUnion) { _uL = logicalBounds.X; _uT = logicalBounds.Y; _uR = logicalBounds.Right; _uB = logicalBounds.Bottom; _hasUnion = true; }
        else
        {
            if (logicalBounds.X < _uL) _uL = logicalBounds.X;
            if (logicalBounds.Y < _uT) _uT = logicalBounds.Y;
            if (logicalBounds.Right > _uR) _uR = logicalBounds.Right;
            if (logicalBounds.Bottom > _uB) _uB = logicalBounds.Bottom;
        }
        return true;
    }

    // Pack a gradient brush + world + local bounds into one gradient instance record (stops/geometry via the shared
    // GradientBake; Params = (type, spread, stopCount, _) to match the gradient-fill vertex shader).
    private static GradientGeometryInstance BuildGradientInstance(GradientBrush g, Matrix4x4F local, Rect localBounds,
        double opacity, int transformSlot, int fadeSlot, int clipSlot)
    {
        var inst = new GradientGeometryInstance { Local = local, Clip = new Vector4F(clipSlot, 0, 0, 0) };
        var alpha = (float)(g.Opacity * opacity);
        Span<Vector4F> cols = stackalloc Vector4F[GradientBake.MaxStops];
        Span<float> offs = stackalloc float[GradientBake.MaxStops];
        var count = GradientBake.PackStops(g, alpha, cols, offs);
        inst.Stop0 = new Color(cols[0]); inst.Stop1 = new Color(cols[1]); inst.Stop2 = new Color(cols[2]); inst.Stop3 = new Color(cols[3]);
        inst.Stop4 = new Color(cols[4]); inst.Stop5 = new Color(cols[5]); inst.Stop6 = new Color(cols[6]); inst.Stop7 = new Color(cols[7]);
        inst.Offsets0 = new Vector4F(offs[0], offs[1], offs[2], offs[3]);
        inst.Offsets1 = new Vector4F(offs[4], offs[5], offs[6], offs[7]);
        var type = GradientBake.PackGeometry(g, out var geom0, out var geom1);
        inst.Geom0 = geom0;
        geom1.W = transformSlot;   // .xy is the radial focal; .w carries the slot, as in the SDF gradient record
        inst.Geom1 = geom1;        // .z stays the SHAPE FLAG the pixel shader branches on - not a spare
        inst.LocalBounds = new Vector4F((float)localBounds.X, (float)localBounds.Y, (float)localBounds.Width, (float)localBounds.Height);
        // .w packs the interp mode (0 sRGB / 1 OKLab) with the OPACITY slot above it - this record has no spare
        // component, and the one that looked spare (Geom1.z) is the shape flag. Unpacked in GradGeomFadeSlot/Interp.
        inst.Params = new Vector4F(type, (float)g.SpreadMethod, count,
            (float)g.ColorInterpolationMode + 2f * (fadeSlot + 1));
        return inst;
    }

    public bool CanBatchTextured(GeometryRenderUnit unit) => unit.TryGetInstancedTexturedFill(out _, out _, out _, out _, out _, out _);

    /// <summary>Collect one instanceable TEXTURED fill. The texture is bound per DRAW, so a change of texture inside one
    /// mesh ends the current run rather than the segment - the instances stay contiguous and each run remembers what to
    /// bind. False if it can't be batched (no frozen mesh, or the source is still decoding).</summary>
    public bool TryAddTextured(GeometryRenderUnit unit, Matrix4x4F local, Rect2D scissor, Rect logicalBounds, int transformSlot, int clipSlot = -1)
    {
        if (!unit.TryGetInstancedTexturedFill(out var key, out var meshObj, out var brush, out var localBounds, out var opacity, out var texture)) return false;
        local = unit.PlaceBaked(local);
        if (meshObj is not FrozenMesh mesh) return false;
        var seg = GetOrCreate(key, mesh);
        if (seg == null) return false;

        // A second texture on the SAME mesh has to start its own run: one draw binds one texture.
        if (seg.PendingTexture != null && seg.PendingTexture != texture && seg.TexCount > seg.TexFlushed) return false;

        if (seg.TexCount + 1 > seg.TexItems.Length) Array.Resize(ref seg.TexItems, seg.TexItems.Length * 2);
        if (seg.TexGpu == null)
        {
            var copies = (int)Math.Max(1u, _device.MaxFramesInFlight);
            if (seg.TexGpuRing != null) DeferRing(seg.TexGpuRing);
            seg.TexGpuRing = new Buffer[copies];
            for (var i = 0; i < copies; i++)
            {
                seg.TexGpuRing[i] = Buffer.New<TextureGeometryInstance>(_device, (uint)seg.TexItems.Length,
                    BufferUsageFlags.StorageBuffer | BufferUsageFlags.ShaderDeviceAddress, Mem);
            }
            seg.TexGpu = seg.TexGpuRing[_copy % copies];
            seg.TexGpuCapacity = seg.TexItems.Length;
            seg.TexRecreated = true;
        }
        if (seg.TexCount + 1 > seg.TexGpuCapacity) return false;

        seg.TexItems[seg.TexCount++] = BuildTexturedInstance(brush, local, localBounds, opacity, transformSlot, FadeSlotFor(unit), clipSlot);
        seg.PendingTexture = texture;
        seg.PendingSampler = ImageSamplers.Of(brush);

        _scissor = scissor;
        if (!seg.InPending) { seg.InPending = true; _pendingKeys.Add(seg); }
        AddPendingUnit(unit);
        NoteFringed(unit, logicalBounds);
        if (!_hasUnion) { _uL = logicalBounds.X; _uT = logicalBounds.Y; _uR = logicalBounds.Right; _uB = logicalBounds.Bottom; _hasUnion = true; }
        else
        {
            if (logicalBounds.X < _uL) _uL = logicalBounds.X;
            if (logicalBounds.Y < _uT) _uT = logicalBounds.Y;
            if (logicalBounds.Right > _uR) _uR = logicalBounds.Right;
            if (logicalBounds.Bottom > _uB) _uB = logicalBounds.Bottom;
        }
        return true;
    }

    // Pack an ImageBrush + world + local bounds into one textured instance. The tiling arithmetic is the SAME one the
    // SDF textured batch uses (ImageTiling), fed the shape's LOCAL box - the geometry PS works in local mesh coords, so
    // the drawn rect is expressed as a fraction of that box rather than in device pixels.
    private static TextureGeometryInstance BuildTexturedInstance(TileBrush brush, Matrix4x4F local, Rect localBounds,
        double opacity, int transformSlot, int fadeSlot, int clipSlot)
    {
        var box = localBounds.Width > 0 && localBounds.Height > 0 ? localBounds : new Rect(0, 0, 1, 1);
        var layout = ImageTiling.Layout(brush, box, local.M11, local.M22, TextureBatchCollector.SourceIsSlice(brush));

        var tint = RectBatchCollector.WithOpacity(brush.Tint, opacity * brush.Opacity);

        return new TextureGeometryInstance
        {
            Local = local,
            Params = new Vector4F(layout.Repeats ? 1f : 0f, layout.Mirror, fadeSlot, transformSlot),   // .z was unused
            LocalBounds = new Vector4F((float)box.X, (float)box.Y, (float)box.Width, (float)box.Height),
            Tile = layout.Tile,
            Rotation = layout.Rotation,
            Drawn = layout.Drawn,
            UvRect = layout.UvRect,
            Tint = tint,
            Clip = new Vector4F(clipSlot, 0, 0, 0)   // the rounded ancestor clip, -1 = none
        };
    }

    public bool CanBatchPattern(GeometryRenderUnit unit) => unit.TryGetInstancedPatternFill(out _, out _, out _, out _, out _);

    /// <summary>Collect one instanceable PATTERN/NOISE fill: append its per-instance world + pattern to its key's pattern
    /// buffer, and register the unit for a deferred fringe/stroke draw. False if it can't be batched.</summary>
    public bool TryAddPattern(GeometryRenderUnit unit, Matrix4x4F local, Rect2D scissor, Rect logicalBounds, int transformSlot, int clipSlot = -1)
    {
        if (!unit.TryGetInstancedPatternFill(out var key, out var meshObj, out var brush, out var localBounds, out var opacity)) return false;
        local = unit.PlaceBaked(local);
        if (meshObj is not FrozenMesh mesh) return false;
        var seg = GetOrCreate(key, mesh);
        if (seg == null) return false;

        if (seg.PatCount + 1 > seg.PatItems.Length) Array.Resize(ref seg.PatItems, seg.PatItems.Length * 2);
        if (seg.PatGpu == null)
        {
            var copies = (int)Math.Max(1u, _device.MaxFramesInFlight);
            if (seg.PatGpuRing != null) DeferRing(seg.PatGpuRing);
            seg.PatGpuRing = new Buffer[copies];
            for (var i = 0; i < copies; i++)
            {
                seg.PatGpuRing[i] = Buffer.New<PatternGeometryInstance>(_device, (uint)seg.PatItems.Length,
                    BufferUsageFlags.StorageBuffer | BufferUsageFlags.ShaderDeviceAddress, Mem);
            }
            seg.PatGpu = seg.PatGpuRing[_copy % copies];
            seg.PatGpuCapacity = seg.PatItems.Length;
            seg.PatRecreated = true;
        }
        if (seg.PatCount + 1 > seg.PatGpuCapacity) return false;

        seg.PatItems[seg.PatCount++] = BuildPatternInstance(brush, local, localBounds, opacity, transformSlot, FadeSlotFor(unit), clipSlot);

        _scissor = scissor;
        if (!seg.InPending) { seg.InPending = true; _pendingKeys.Add(seg); }
        AddPendingUnit(unit);
        NoteFringed(unit, logicalBounds);
        if (!_hasUnion) { _uL = logicalBounds.X; _uT = logicalBounds.Y; _uR = logicalBounds.Right; _uB = logicalBounds.Bottom; _hasUnion = true; }
        else
        {
            if (logicalBounds.X < _uL) _uL = logicalBounds.X;
            if (logicalBounds.Y < _uT) _uT = logicalBounds.Y;
            if (logicalBounds.Right > _uR) _uR = logicalBounds.Right;
            if (logicalBounds.Bottom > _uB) _uB = logicalBounds.Bottom;
        }
        return true;
    }

    // Pack a PatternBrush/NoiseBrush + world + local bounds into one pattern instance record. Cell stays in LOCAL units (the
    // geometry PS works in local mesh coords) - no device-scale, unlike the SDF rect bake. Mirrors PatternRectCollector.BakeItem.
    private static PatternGeometryInstance BuildPatternInstance(Brush brush, Matrix4x4F local, Rect localBounds,
        double opacity, int transformSlot, int fadeSlot, int clipSlot)
    {
        var inst = new PatternGeometryInstance { Local = local };
        PatternBrushRecord.TryDescribe(brush, out var record);   // the caller already refused anything else

        var alpha = (float)(opacity * record.Opacity);
        var c1 = record.Color1.ToVector4(); c1.W *= alpha;
        var c2 = record.Color2.ToVector4(); c2.W *= alpha;
        var c3 = record.MidColor.ToVector4(); c3.W *= alpha;
        var noise = record.Noise;

        inst.Params = new Vector4F(fadeSlot, record.Type, (float)record.Cell, transformSlot);   // .x was unused
        inst.LocalBounds = new Vector4F((float)localBounds.X, (float)localBounds.Y, (float)localBounds.Width, (float)localBounds.Height);
        inst.Color1 = c1;
        inst.Color2 = c2;
        inst.Color3 = c3;
        inst.Noise = noise;
        // .w = the rounded ancestor clip's slot (-1 = none), read in the vertex stage; .z is unused by either carrier.
        inst.Anim = new Vector4F((float)record.PhaseOffset, (float)record.FrozenPhase, 0, clipSlot);
        return inst;
    }

    public bool CanBatchMaterial(GeometryRenderUnit unit) => unit.TryGetInstancedMaterialFill(out _, out _, out _, out _, out _);

    /// <summary>
    /// Collect one instanceable BACKDROP MATERIAL fill on tessellated geometry - a star, an icon, any authored outline.
    ///
    /// <para>A run must agree about the image and the pass that reads it (a disagreement is refused, so the caller
    /// flushes); the captured region merely GROWS to cover every instance in it.</para></summary>
    public bool TryAddMaterial(GeometryRenderUnit unit, Matrix4x4F local, Rect2D scissor, Rect logicalBounds,
        int transformSlot, Rect2D captureRegion, int clipSlot = -1)
    {
        if (!unit.TryGetInstancedMaterialFill(out var key, out var meshObj, out var brush, out var localBounds, out var opacity)) return false;
        local = unit.PlaceBaked(local);
        if (meshObj is not FrozenMesh mesh) return false;
        var seg = GetOrCreate(key, mesh);
        if (seg == null) return false;

        var wallpaper = MaterialRectCollector.IsWallpaper(brush.Material);
        var treatment = MaterialRectCollector.TreatmentOf(brush.Material);
        var source = unit.BrushTexture();
        var anchor = brush.Anchor;
        if (seg.MatPending && seg.MatCount > seg.MatFlushed
            && (seg.MatWallpaper != wallpaper || seg.MatTreatment != treatment || !ReferenceEquals(seg.MatSource, source)
                || (source != null && seg.MatAnchor != anchor))) return false;

        if (seg.MatCount + 1 > seg.MatItems.Length) Array.Resize(ref seg.MatItems, seg.MatItems.Length * 2);
        if (seg.MatGpu == null)
        {
            var copies = (int)Math.Max(1u, _device.MaxFramesInFlight);
            if (seg.MatGpuRing != null) DeferRing(seg.MatGpuRing);
            seg.MatGpuRing = new Buffer[copies];
            for (var i = 0; i < copies; i++)
            {
                seg.MatGpuRing[i] = Buffer.New<PatternGeometryInstance>(_device, (uint)seg.MatItems.Length,
                    BufferUsageFlags.StorageBuffer | BufferUsageFlags.ShaderDeviceAddress, Mem);
            }
            seg.MatGpu = seg.MatGpuRing[_copy % copies];
            seg.MatGpuCapacity = seg.MatItems.Length;
            seg.MatRecreated = true;
        }
        if (seg.MatCount + 1 > seg.MatGpuCapacity) return false;

        seg.MatItems[seg.MatCount++] = BuildMaterialInstance(brush, local, localBounds, opacity, transformSlot,
            FadeSlotFor(unit), source != null && anchor == MaterialAnchor.Element, clipSlot);
        seg.MatRegion = seg.MatPending ? Union(seg.MatRegion, captureRegion) : captureRegion;
        seg.MatWallpaper = wallpaper;
        seg.MatTreatment = treatment;
        seg.MatSource = source;
        seg.MatAnchor = anchor;
        seg.MatPending = true;

        _scissor = scissor;
        if (!seg.InPending) { seg.InPending = true; _pendingKeys.Add(seg); }
        AddPendingUnit(unit);
        NoteFringed(unit, logicalBounds);
        if (!_hasUnion) { _uL = logicalBounds.X; _uT = logicalBounds.Y; _uR = logicalBounds.Right; _uB = logicalBounds.Bottom; _hasUnion = true; }
        else
        {
            if (logicalBounds.X < _uL) _uL = logicalBounds.X;
            if (logicalBounds.Y < _uT) _uT = logicalBounds.Y;
            if (logicalBounds.Right > _uR) _uR = logicalBounds.Right;
            if (logicalBounds.Bottom > _uB) _uB = logicalBounds.Bottom;
        }
        return true;
    }

    private static Rect2D Union(Rect2D a, Rect2D b)
    {
        if (a.Extent.Width == 0 || a.Extent.Height == 0) return b;
        if (b.Extent.Width == 0 || b.Extent.Height == 0) return a;
        var x = Math.Min(a.Offset.X, b.Offset.X);
        var y = Math.Min(a.Offset.Y, b.Offset.Y);
        var right = Math.Max(a.Offset.X + (int)a.Extent.Width, b.Offset.X + (int)b.Extent.Width);
        var bottom = Math.Max(a.Offset.Y + (int)a.Extent.Height, b.Offset.Y + (int)b.Extent.Height);
        return new Rect2D
        {
            Offset = new Offset2D { X = x, Y = y },
            Extent = new Extent2D { Width = (uint)(right - x), Height = (uint)(bottom - y) }
        };
    }

    // The same PatternGeomData slots the pattern bake fills, carrying the material's numbers: tint in Color1 (alpha = its
    // strength), blur / grain / refraction in Color3 - as MaterialRectCollector bakes them for the analytic shapes.
    // The light's three numbers plus the clip slot in the fourth: this carrier has one field left and both need it.
    private static Vector4F LightWithClip(MaterialBrush brush, int clipSlot)
    {
        var light = MaterialRectCollector.LightOf(brush);
        return new Vector4F(light.X, light.Y, light.Z, clipSlot);
    }

    private static PatternGeometryInstance BuildMaterialInstance(MaterialBrush brush, Matrix4x4F local, Rect localBounds,
        double opacity, int transformSlot, int fadeSlot, bool pinnedToElement, int clipSlot)
    {
        var tint = brush.TintColor;

        return new PatternGeometryInstance
        {
            Local = local,
            // .y says the picture is pinned to the ELEMENT, and the shader then takes its coordinates from the mesh's
            // own local bounds instead of from the frame.
            Params = new Vector4F(fadeSlot, pinnedToElement ? 1f : 0f, 0, transformSlot),
            LocalBounds = new Vector4F((float)localBounds.X, (float)localBounds.Y, (float)localBounds.Width, (float)localBounds.Height),
            // The tint's ALPHA carries TintOpacity - how much the tint covers the capture - while the element's own
            // opacity rides the fill's alpha in Color3.w, since here the coverage is the geometry rather than a field.
            Color1 = new Vector4F(tint.R / 255f, tint.G / 255f, tint.B / 255f, (float)Math.Clamp(brush.TintOpacity, 0.0, 1.0)),
            // The NAP, in the three fields a material otherwise leaves empty. Same numbers as the SDF carrier bakes,
            // from the same helpers, so a velvet path and a velvet rectangle wear one cloth.
            Color2 = MaterialRectCollector.SurfaceOf(brush),
            Color3 = new Vector4F(MaterialRectCollector.BlurTexels(brush), (float)brush.NoiseAmount, (float)brush.Refraction,
                (float)Math.Clamp(opacity * brush.Opacity, 0.0, 1.0)),
            Noise = MaterialRectCollector.ResponseOf(brush),
            // .xyz the nap and the light; .w = the rounded ancestor clip's slot (-1 = none): this carrier shares
            // PatternGeomData with the pattern fill, so it reads the clip from the same place - see BuildPatternInstance.
            Anim = LightWithClip(brush, clipSlot)
        };
    }

    // Register a collected unit for the deferred per-unit draw - but ONLY if it still has something to draw there. A
    // solid fill whose fringe is instanced too has nothing left (its body is skipped via FillInstanced), and that empty
    // Render() per element is exactly the cost this path exists to remove.
    private void AddPendingUnit(GeometryRenderUnit unit)
    {
        if (!unit.HasPerUnitOverlay) return;
        _pendingUnits.Add(unit);
    }

    /// <summary>Does a unit's logical bounds overlap the pending group? A later overlapping non-batched unit must draw
    /// AFTER a flush so it paints on top.</summary>
    public bool OverlapsPending(Rect r)
        => _hasUnion && r.X < _uR && _uL < r.Right && r.Y < _uB && _uT < r.Bottom;

    // Union of pending shapes that WEAR a fringe, kept apart from the union of all of them: only a fringed pending shape
    // can band a later fill.
    private double _fL, _fT, _fR, _fB;
    private bool _hasFringeUnion;

    /// <summary>Does this shape overlap a pending shape whose fringe would be drawn after it?</summary>
    public bool OverlapsPendingFringe(Rect r)
        => _hasFringeUnion && r.X < _fR && _fL < r.Right && r.Y < _fB && _fT < r.Bottom;

    private void NoteFringed(GeometryRenderUnit unit, Rect logicalBounds)
    {
        if (!unit.HasInstancedFringe && !unit.HasPerUnitOverlay) return;

        if (!_hasFringeUnion)
        {
            _fL = logicalBounds.X; _fT = logicalBounds.Y; _fR = logicalBounds.Right; _fB = logicalBounds.Bottom;
            _hasFringeUnion = true;
            return;
        }

        if (logicalBounds.X < _fL) _fL = logicalBounds.X;
        if (logicalBounds.Y < _fT) _fT = logicalBounds.Y;
        if (logicalBounds.Right > _fR) _fR = logicalBounds.Right;
        if (logicalBounds.Bottom > _fB) _fB = logicalBounds.Bottom;
    }

    /// <summary>Whether a group drawn since this was last cleared holds a noise that flows with time alone.</summary>
    public bool DrewMoving { get; set; }

    // A flowing noise carries its animate flag in the sign of its octaves; a pattern's pack means something else there.
    private static bool Flows(ReadOnlySpan<PatternGeometryInstance> items)
    {
        foreach (ref readonly var item in items)
        {
            if (item.Params.Y >= PatternBrushRecord.NoiseBase && item.Noise.X < 0f) return true;
        }

        return false;
    }

    /// <summary>Draw the pending clip group: each key's new instances as one instanced call (fills), then the collected
    /// units' fringe/stroke ON TOP (fill-under-fringe). Records the group into a pooled <see cref="FlushRecord"/> and
    /// draws THROUGH it (so the immediate draw and a later clean-frame replay share one path). Returns the record index
    /// for the op stream, or -1 if nothing was pending. Restores <paramref name="fullScissor"/> for the caller.</summary>
    public int Flush(Rect2D fullScissor, Matrix4x4F projection)
    {
        if (_pendingKeys.Count == 0 && _pendingUnits.Count == 0) return -1;

        PrepareRetainedWrite();

        // Record this group's draws + upload each key's new instances NOW (recording frame). The record captures the
        // buffer range (first,count) per key so replay re-issues the exact same draw with no upload.
        var rec = TakeFlushRecord();
        rec.Scissor = _scissor;
        // Clamped to the field, NOT left to wrap: a mark past it writes back as 0 through the write mask, and a fringe
        // testing Greater against 0 draws nowhere. Past exhaustion the caller has already stopped relying on marks.
        // The group takes a RANGE of marks - one per key, handed out in paint order by MarkFor - so the counter is
        // advanced by however many keys the group ends up with, below.
        rec.StencilRef = Math.Min(++_groupRef, CoverageMarkBits);

        foreach (var seg in _pendingKeys)
        {
            var count = seg.Count - seg.Flushed;
            if (count > 0)
            {
                // Incremental upload: skip on a clean frame (retained buffer already holds these bytes), but never on a
                // buffer (re)allocated this frame. Offset the BDA by firstInstance so SV_InstanceID starts at 0.
                if (!SceneClean || seg.Recreated)
                    seg.Gpu.SetData(seg.Items.AsSpan(seg.Flushed, count), (uint)(seg.Flushed * InstanceStride));
                rec.Keys.Add((seg, (uint)seg.Flushed, (uint)count));
                seg.Flushed = seg.Count;
            }
            var gcount = seg.GradCount - seg.GradFlushed;
            if (gcount > 0)
            {
                if (!SceneClean || seg.GradRecreated)
                    seg.GradGpu.SetData(seg.GradItems.AsSpan(seg.GradFlushed, gcount), (uint)(seg.GradFlushed * GradInstanceStride));
                rec.GradKeys.Add((seg, (uint)seg.GradFlushed, (uint)gcount));
                seg.GradFlushed = seg.GradCount;
            }
            var pcount = seg.PatCount - seg.PatFlushed;
            if (pcount > 0)
            {
                if (!SceneClean || seg.PatRecreated)
                    seg.PatGpu.SetData(seg.PatItems.AsSpan(seg.PatFlushed, pcount), (uint)(seg.PatFlushed * PatInstanceStride));
                // Cut the run wherever the KIND changes: one pass evaluates one field, so a draw may not span two.
                // Params.Y is the kind the CPU baked (PatternType / NoiseType), the same number the SDF path keys on.
                var runStart = seg.PatFlushed;
                var runKind = (int)seg.PatItems[runStart].Params.Y;
                for (var k = seg.PatFlushed + 1; k < seg.PatCount; k++)
                {
                    var kk = (int)seg.PatItems[k].Params.Y;
                    if (kk == runKind) continue;
                    rec.PatKeys.Add((seg, (uint)runStart, (uint)(k - runStart), runKind));
                    runStart = k;
                    runKind = kk;
                }
                rec.PatKeys.Add((seg, (uint)runStart, (uint)(seg.PatCount - runStart), runKind));
                seg.PatFlushed = seg.PatCount;
            }
            var tcount = seg.TexCount - seg.TexFlushed;
            if (tcount > 0)
            {
                if (!SceneClean || seg.TexRecreated)
                    seg.TexGpu.SetData(seg.TexItems.AsSpan(seg.TexFlushed, tcount), (uint)(seg.TexFlushed * TexInstanceStride));
                rec.TexKeys.Add((seg, (uint)seg.TexFlushed, (uint)tcount, seg.PendingTexture,
                    seg.PendingSampler ?? SamplerStates.LinearClampToEdge));
                seg.TexFlushed = seg.TexCount;
                seg.PendingTexture = null;   // the run is closed; the next texture starts a fresh one
            }
            var mcount = seg.MatCount - seg.MatFlushed;
            if (mcount > 0)
            {
                if (!SceneClean || seg.MatRecreated)
                    seg.MatGpu.SetData(seg.MatItems.AsSpan(seg.MatFlushed, mcount), (uint)(seg.MatFlushed * PatInstanceStride));
                rec.MatKeys.Add((seg, (uint)seg.MatFlushed, (uint)mcount, seg.MatWallpaper, seg.MatTreatment, seg.MatRegion,
                    seg.MatSource, seg.MatAnchor));
                seg.MatFlushed = seg.MatCount;
                seg.MatPending = false;   // the run is closed; the next source starts a fresh one
            }
            seg.InPending = false;
        }
        foreach (var u in _pendingUnits) rec.Units.Add(u);

        // The group has taken one mark per key, so the NEXT group starts past all of them - two groups sharing a mark
        // would put the later one's fringes under the earlier one's fills.
        if (rec.Keys.Count > 1) _groupRef += (uint)(rec.Keys.Count - 1);

        _pendingKeys.Clear();
        _pendingUnits.Clear();
        _hasUnion = false;
        _hasFringeUnion = false;

        DrawFlushRecord(rec, fullScissor, projection);
        return _flushCount - 1;
    }

    /// <summary>Asked before each deferred fringe/stroke is drawn. A per-unit overlay bakes its transform at record time,
    /// so on a frame where something MOVED it has to be re-pointed - exactly as a recorded per-unit draw is. Set by the
    /// render cache, which is the only one that knows what moved.</summary>
    public Action<IRenderUnit> PrepareOverlay { get; set; }

    /// <summary>Re-issue a flush recorded earlier this cycle (a clean-frame replay): same key draws + deferred unit
    /// fringe/stroke, NO re-upload (the retained instance buffers still hold the bytes).</summary>
    public void ReplayFlush(int index, Rect2D fullScissor, Matrix4x4F projection)
        => DrawFlushRecord(_flushRecords[index], fullScissor, projection);

    /// <summary>Whether a recorded flush leaves the render pass: its backdrop materials copy the frame out of it, its
    /// curve tiles are binned outside it.</summary>
    public bool FlushLeavesRenderPass(int index)
    {
        if (index < 0 || index >= _flushRecords.Count) return false;

        var rec = _flushRecords[index];
        if (rec.MatKeys.Count > 0) return true;
        if (!CurveTilesEnabled) return false;

        foreach (var key in rec.Keys) if (key.Seg.CurveBuffer != null) return true;
        foreach (var key in rec.GradKeys) if (key.Seg.CurveBuffer != null) return true;
        foreach (var key in rec.PatKeys) if (key.Seg.CurveBuffer != null) return true;
        foreach (var key in rec.TexKeys) if (key.Seg.CurveBuffer != null) return true;
        foreach (var key in rec.MatKeys) if (key.Seg.CurveBuffer != null) return true;
        return false;
    }

    /// <summary>Give a recorded flush a freshly derived clip - its scissor is a world-space rect frozen when it flushed,
    /// so a viewport that moved since describes a different frame (see RenderCache.RefreshMovedScissors).</summary>
    public void SetFlushScissor(int index, Rect2D scissor)
    {
        if (index >= 0 && index < _flushRecords.Count) _flushRecords[index].Scissor = scissor;
    }

    private FlushRecord AddFlushRecord() { var r = new FlushRecord(); _flushRecords.Add(r); return r; }

    // Asked BEFORE the state is set, not discovered inside the loop. A pipeline, five stencil writes and two scissor
    // sets went in ahead of a loop that then skipped every single entry - a mesh with no closed boundary has no fringe,
    // and a textured run with no texture is not drawn at all. Checking a handful of list entries is the cheap half.
    private static bool AnyRing(List<(KeySegment Seg, uint First, uint Count)> keys)
    {
        foreach (var k in keys)
            if (k.Seg.RingBuffer != null) return true;
        return false;
    }

    // Same question for the kind-tagged pattern runs.
    private static bool AnyRing(List<(KeySegment Seg, uint First, uint Count, int Kind)> keys)
    {
        foreach (var k in keys)
            if (k.Seg.RingBuffer != null) return true;
        return false;
    }

    private static bool AnyDrawableRing(List<(KeySegment Seg, uint First, uint Count, ITexture Texture, SamplerState Sampler)> keys)
    {
        foreach (var k in keys)
            if (k.Seg.RingBuffer != null && k.Texture != null) return true;
        return false;
    }

    private static bool AnyTexture(List<(KeySegment Seg, uint First, uint Count, ITexture Texture, SamplerState Sampler)> keys)
    {
        foreach (var k in keys)
            if (k.Texture != null) return true;
        return false;
    }

    /// <summary>The next LIVE flush record, blank and counted. Records are pooled and reused from index 0 by every
    /// recording walk, so the ones the current frame is made of are exactly <c>[0, _flushCount)</c> - everything past
    /// that is a leftover from some longer frame. Both the walk and a patch take their record through here, so a patch's
    /// flush is part of the frame it is patching instead of landing in the dead tail where no recorded op draws it.</summary>
    private FlushRecord TakeFlushRecord()
    {
        var rec = _flushCount < _flushRecords.Count ? _flushRecords[_flushCount] : AddFlushRecord();
        _flushCount++;
        rec.Reset();
        return rec;
    }

    private void DrawFlushRecord(FlushRecord rec, Rect2D fullScissor, Matrix4x4F projection)
    {
        // Every run's curve fills binned at once, before any of them draws: the tiles are the flush's.
        var tiles = CurveTilesEnabled && BinCurveTiles(rec, projection) ? rec.Tiles : null;
        if (rec.Keys.Count > 0)
        {
            UseTiles(tiles, _effect, _effect.CurveTiled);
            SetupInstancedState(projection);

            // Stamp this group's coverage mark wherever a fill lands, so the fringes below can tell "my group's fill"
            // from "an earlier group's, which I belong on top of".
            _device.StencilTestEnabled = true;
            _device.StencilCompareOp = CompareOp.Always;
            _device.StencilPassOp = StencilOp.Replace;
            _device.StencilWriteMask = 0xFF;
            _device.StencilReference = rec.StencilRef;

            _device.SetScissors(rec.Scissor);
            for (var i = 0; i < rec.Keys.Count; i++)
            {
                var (seg, first, count) = rec.Keys[i];
                // Its own mark, not the group's: a key stamps where it sits in the paint order, so the fringes below
                // can tell what is under them from what is over them.
                _device.StencilReference = rec.MarkFor(i);
                _effect.Geometries.SetValue(seg.Gpu.GetDeviceAddress() + (ulong)(first * InstanceStride));
                if (seg.CurveBuffer != null)
                {
                    TileKey(tiles, _effect.CurveTiled, _effect.CurveTileItemBase, _solidTileBase[i]);
                    DrawCurves(seg, count, _effect.BatchCurveFillPass, _effect.CurveData, _effect.ViewportSize);
                    continue;
                }

                _device.VertexType = typeof(UIVertex);
                _device.SetVertexBuffer(seg.VtxBuffer);
                _device.PrimitiveTopology = seg.Topology;
                _effect.BatchFillPass.Apply();
                if (seg.Indexed)
                    _device.DrawIndexed(seg.VtxBuffer, seg.IdxBuffer, instanceCount: count, indexCount: seg.IndexCount);
                else
                    _device.Draw(seg.VertexCount, count);
            }
            _device.SetScissors(fullScissor);

            _device.StencilTestEnabled = false;
        }

        // The analytic-AA fringe of those same instances: the shared ring per key, drawn with the SAME instance buffer
        // range as the body above, one draw per key. This is what the deferred per-unit fringe loop used to do one
        // element at a time (a pipeline switch + a uniform matrix each), which measured ~90% of the draw phase.
        if (rec.Keys.Count > 0 && AnalyticAa.Enabled && AnyRing(rec.Keys))
        {
            SetupFringeState(projection);

            // The ring feathers OUTSIDE its own shape, so it has no business anywhere a fill of this group already
            // covers. Masking it there is what makes the group's [all fills][all fringes] order indistinguishable from
            // true paint order - and with that, two overlapping shapes no longer have to be split into separate groups.
            // Earlier groups carry a LOWER mark and lie underneath, so the fringe still draws over them.
            _device.StencilTestEnabled = true;
            _device.StencilCompareOp = CompareOp.Greater;   // draw where THIS key's mark is greater than what is there
            _device.StencilPassOp = StencilOp.Keep;
            _device.StencilWriteMask = 0;

            _device.SetScissors(rec.Scissor);
            for (var i = 0; i < rec.Keys.Count; i++)
            {
                var (seg, first, count) = rec.Keys[i];
                if (seg.RingBuffer == null) continue;   // a mesh with no closed boundary has no fringe
                // Tested against the key's OWN mark: lower marks are what this key was drawn on top of, so its fringe
                // belongs there; its own fill and every later key of the group carry a mark at least this high and cut
                // it, which is what keeps [all fills][all fringes] indistinguishable from true paint order.
                _device.StencilReference = rec.MarkFor(i);
                _effect.Geometries.SetValue(seg.Gpu.GetDeviceAddress() + (ulong)(first * InstanceStride));
                _effect.RingEdges.SetValue(seg.RingBuffer.GetDeviceAddress());
                _effect.BatchFringePass.Apply();
                _device.Draw(seg.RingVertexCount, count);
            }
            _device.SetScissors(fullScissor);

            _device.StencilTestEnabled = false;
            _device.StencilWriteMask = 0xFF;
        }

        // Gradient instanced fills for this group (same shared meshes, gradient pass + per-instance gradient buffer),
        // drawn after the solid fills in the same clip. State is the same as the solid fill; only the pass differs.
        if (rec.GradKeys.Count > 0)
        {
            SetupInstancedState(projection);
            var brush = Brush(projection);
            UseTiles(tiles, brush, brush.CurveTiled);
            _device.SetScissors(rec.Scissor);
            for (var k = 0; k < rec.GradKeys.Count; k++)
            {
                var (seg, first, count) = rec.GradKeys[k];
                brush.GradientGeometries.SetValue(seg.GradGpu.GetDeviceAddress() + (ulong)(first * GradInstanceStride));
                if (seg.CurveBuffer != null)
                {
                    TileKey(tiles, brush.CurveTiled, brush.CurveTileItemBase, _gradTileBase[k]);
                    DrawCurves(seg, count, brush.GradientCurvePass, brush.CurveData, brush.ViewportSize);
                    continue;
                }

                _device.VertexType = typeof(UIVertex);
                _device.SetVertexBuffer(seg.VtxBuffer);
                _device.PrimitiveTopology = seg.Topology;
                brush.GradientMeshPass.Apply();
                if (seg.Indexed)
                    _device.DrawIndexed(seg.VtxBuffer, seg.IdxBuffer, instanceCount: count, indexCount: seg.IndexCount);
                else
                    _device.Draw(seg.VertexCount, count);
            }
            _device.SetScissors(fullScissor);
        }

        // Pattern/noise instanced fills for this group (same shared meshes, pattern-fill pass + per-instance pattern buffer).
        if (rec.PatKeys.Count > 0)
        {
            SetupInstancedState(projection);
            var brush = Brush(projection);
            brush.Time.SetValue((float)NoiseClock.Time);   // animated noise reads the shared flow clock
            UseTiles(tiles, brush, brush.CurveTiled);
            _device.SetScissors(rec.Scissor);
            for (var k = 0; k < rec.PatKeys.Count; k++)
            {
                var (seg, first, count, kind) = rec.PatKeys[k];
                if (!DrewMoving) DrewMoving = Flows(seg.PatItems.AsSpan((int)first, (int)count));
                brush.PatternGeometries.SetValue(seg.PatGpu.GetDeviceAddress() + (ulong)(first * PatInstanceStride));
                if (seg.CurveBuffer != null)
                {
                    TileKey(tiles, brush.CurveTiled, brush.CurveTileItemBase, _patTileBase[k]);
                    DrawCurves(seg, count, CurvePassFor(brush, kind), brush.CurveData, brush.ViewportSize);
                    continue;
                }

                _device.VertexType = typeof(UIVertex);
                _device.SetVertexBuffer(seg.VtxBuffer);
                _device.PrimitiveTopology = seg.Topology;
                MeshPassFor(brush, kind).Apply();
                if (seg.Indexed)
                    _device.DrawIndexed(seg.VtxBuffer, seg.IdxBuffer, instanceCount: count, indexCount: seg.IndexCount);
                else
                    _device.Draw(seg.VertexCount, count);
            }
            _device.SetScissors(fullScissor);
        }

        // Textured instanced fills for this group (same shared meshes, textured pass + per-instance textured buffer).
        // ONE texture per draw, so each run binds its own - a run ends where the texture changes.
        if (rec.TexKeys.Count > 0 && AnyTexture(rec.TexKeys))
        {
            SetupInstancedState(projection);
            var brush = Brush(projection);
            UseTiles(tiles, brush, brush.CurveTiled);
            _device.SetScissors(rec.Scissor);
            for (var k = 0; k < rec.TexKeys.Count; k++)
            {
                var (seg, first, count, texture, sampler) = rec.TexKeys[k];
                // NO texture, NO draw: the heap path passes a texture as an INDEX written into push data by whoever bound
                // one last, so drawing without binding samples whatever descriptor sits there - in practice the glyph
                // atlas, smeared across the frame. See TextureBatchCollector.DrawSegment.
                if (texture == null) continue;
                brush.TextureGeometries.SetValue(seg.TexGpu.GetDeviceAddress() + (ulong)(first * TexInstanceStride));
                brush.SourceTexture.SetResource(texture);
                brush.SourceSampler.SetResource(sampler);
                if (seg.CurveBuffer != null)
                {
                    TileKey(tiles, brush.CurveTiled, brush.CurveTileItemBase, _texTileBase[k]);
                    DrawCurves(seg, count, brush.TextureCurvePass, brush.CurveData, brush.ViewportSize);
                    continue;
                }

                _device.VertexType = typeof(UIVertex);
                _device.SetVertexBuffer(seg.VtxBuffer);
                _device.PrimitiveTopology = seg.Topology;
                brush.TextureMeshPass.Apply();
                if (seg.Indexed)
                    _device.DrawIndexed(seg.VtxBuffer, seg.IdxBuffer, instanceCount: count, indexCount: seg.IndexCount);
                else
                    _device.Draw(seg.VertexCount, count);
            }
            _device.SetScissors(fullScissor);
        }

        // BACKDROP MATERIALS on tessellated geometry, LAST of this group's fills: each run copies the frame behind it
        // before drawing, so everything meant to show through must already be in the frame. The copy is taken between
        // two draws, hence a source bound per run rather than once for the group.
        if (rec.MatKeys.Count > 0 && Backdrop != null)
        {
            SetupInstancedState(projection);
            var material = Material(projection);
            material.Projection.SetValue(projection);
            material.Transforms.SetValue(TransformsAddress);
            UseTiles(tiles, material, material.CurveTiled);
            _device.SetScissors(rec.Scissor);
            for (var k = 0; k < rec.MatKeys.Count; k++)
            {
                var (seg, first, count, wallpaper, treatment, region, source, anchor) = rec.MatKeys[k];
                // NO backdrop, NO draw - the same refusal the textured runs make, and for the same reason: a pass that
                // samples an unbound descriptor paints whatever was left there. The SURFACES read no backdrop at all,
                // so the refusal does not apply to them: asking would refuse every velvet surface there is.
                if (MaterialRectCollector.NeedsBackdrop(treatment)
                    && !Backdrop.BindSource(_device, wallpaper, region, source, anchor,
                        material.SourceTexture, material.SourceSampler, material.SourceUv)) continue;
                material.MaterialGeometries.SetValue(seg.MatGpu.GetDeviceAddress() + (ulong)(first * PatInstanceStride));
                if (seg.CurveBuffer != null)
                {
                    TileKey(tiles, material.CurveTiled, material.CurveTileItemBase, _matTileBase[k]);
                    DrawCurves(seg, count, treatment switch
                    {
                        Core.Media.MaterialTreatment.Glass => material.MaterialGlassCurvePass,
                        Core.Media.MaterialTreatment.Sheen => material.MaterialSheenCurvePass,
                        Core.Media.MaterialTreatment.Metal => material.MaterialMetalCurvePass,
                        Core.Media.MaterialTreatment.Wood => material.MaterialWoodCurvePass,
                        _ => material.MaterialFrostedCurvePass
                    }, material.CurveData, material.ViewportSize);
                    continue;
                }

                _device.VertexType = typeof(UIVertex);
                _device.SetVertexBuffer(seg.VtxBuffer);
                _device.PrimitiveTopology = seg.Topology;
                (treatment switch
                {
                    Core.Media.MaterialTreatment.Glass => material.MaterialGlassMeshPass,
                    Core.Media.MaterialTreatment.Sheen => material.MaterialSheenMeshPass,
                    Core.Media.MaterialTreatment.Metal => material.MaterialMetalMeshPass,
                    Core.Media.MaterialTreatment.Wood => material.MaterialWoodMeshPass,
                    _ => material.MaterialFrostedMeshPass
                }).Apply();
                if (seg.Indexed)
                    _device.DrawIndexed(seg.VtxBuffer, seg.IdxBuffer, instanceCount: count, indexCount: seg.IndexCount);
                else
                    _device.Draw(seg.VertexCount, count);
            }
            _device.SetScissors(fullScissor);
        }

        // The gradient instances' fringe: shared ring, same instance buffer, colored by the gradient per fragment.
        if (rec.GradKeys.Count > 0 && AnalyticAa.Enabled && AnyRing(rec.GradKeys))
        {
            SetupFringeState(projection);
            var brush = Brush(projection);
            _device.SetScissors(rec.Scissor);
            foreach (var (seg, first, count) in rec.GradKeys)
            {
                if (seg.RingBuffer == null) continue;
                brush.GradientGeometries.SetValue(seg.GradGpu.GetDeviceAddress() + (ulong)(first * GradInstanceStride));
                brush.RingEdges.SetValue(seg.RingBuffer.GetDeviceAddress());
                brush.GradientFringePass.Apply();
                _device.Draw(seg.RingVertexCount, count);
            }
            _device.SetScissors(fullScissor);
        }

        // The pattern/noise instances' fringe, same shape as the solid one above: shared ring, same instance buffer.
        if (rec.PatKeys.Count > 0 && AnalyticAa.Enabled && AnyRing(rec.PatKeys))
        {
            // Drawn through the SHAPE effect, not the brush one: a flat ring is the solid fringe with the brush's low
            // color, so the address goes where the pass does.
            SetupFringeState(projection);
            _device.SetScissors(rec.Scissor);
            foreach (var (seg, first, count, _) in rec.PatKeys)   // the ring is flat-colored: kind does not change the pass
            {
                if (seg.RingBuffer == null) continue;
                _effect.PatternGeometries.SetValue(seg.PatGpu.GetDeviceAddress() + (ulong)(first * PatInstanceStride));
                _effect.RingEdges.SetValue(seg.RingBuffer.GetDeviceAddress());
                _effect.BatchPatternFringePass.Apply();
                _device.Draw(seg.RingVertexCount, count);
            }
            _device.SetScissors(fullScissor);
        }

        // The textured instances' fringe: same ring, same instance buffer, and the SAME texture the body sampled - the
        // ring samples the picture rather than taking one flat color, so the edge is the shape's own edge.
        if (rec.TexKeys.Count > 0 && AnalyticAa.Enabled && AnyDrawableRing(rec.TexKeys))
        {
            SetupFringeState(projection);
            var brush = Brush(projection);
            _device.SetScissors(rec.Scissor);
            foreach (var (seg, first, count, texture, sampler) in rec.TexKeys)
            {
                if (seg.RingBuffer == null || texture == null) continue;
                brush.TextureGeometries.SetValue(seg.TexGpu.GetDeviceAddress() + (ulong)(first * TexInstanceStride));
                brush.SourceTexture.SetResource(texture);
                brush.SourceSampler.SetResource(sampler);
                brush.RingEdges.SetValue(seg.RingBuffer.GetDeviceAddress());
                brush.TextureFringePass.Apply();
                _device.Draw(seg.RingVertexCount, count);
            }
            _device.SetScissors(fullScissor);
        }

        // Deferred fringe/stroke of the collected units, drawn OVER their now-flushed fills, in the same clip. The unit's
        // Render() skips its fill body (FillInstanced) and draws only the analytic-AA fringe + stroke.
        if (rec.Units.Count > 0)
        {
            _device.SetScissors(rec.Scissor);
            foreach (var u in rec.Units)
            {
                PrepareOverlay?.Invoke(u);
                u.Render();
            }
            _device.SetScissors(fullScissor);
        }
    }

    private static bool ScissorSame(Rect2D a, Rect2D b) =>
        a.Offset.X == b.Offset.X && a.Offset.Y == b.Offset.Y &&
        a.Extent.Width == b.Extent.Width && a.Extent.Height == b.Extent.Height;


    // Shared InstancedFill device state (all keys draw the same way; only the mesh topology varies).
    /// <summary>Device address of the shared transform table - the vertex shader fetches each instance's slot matrix from
    /// it. Set by the caller each frame (the table may have been reallocated), same as for the SDF batches.</summary>
    public ulong TransformsAddress { get; set; }

    /// <summary>A transform-table slot's matrix as the CPU holds it - what the curve tiles place each fill by. Set by the
    /// caller, which owns the table.</summary>
    public Func<int, Matrix4x4F> SlotMatrix { get; set; }

    // The binning passes, one set for every flush's tiles - made on the first flush that bins.
    private CurveTileEffect _tileEffect;

    // The flush being binned: its curve fill instances as the tiler reads them, their totals, and the map from a fill's
    // local space to framebuffer pixels before its own matrix.
    private CurveTileItem[] _tileItems = new CurveTileItem[64];
    private int _tileCount;
    private uint _tilePieces;
    private uint _tileTotal;
    private uint _tileRows;
    private Matrix4x4F _tileProjection;
    private Viewport _tileViewport;

    // The totals before the key being binned, to drop it again.
    private int _keyTileCount;
    private uint _keyTilePieces;
    private uint _keyTileTotal;
    private uint _keyTileRows;

    // Per key of each of the flush's runs, its first item among the flush's (-1: not a curve fill).
    private int[] _solidTileBase = new int[16];
    private int[] _gradTileBase = new int[16];
    private int[] _patTileBase = new int[16];
    private int[] _texTileBase = new int[16];
    private int[] _matTileBase = new int[16];

    // The flush's curve fills, every brush's, binned into screen tiles out of the render pass. False when it has none to
    // bin. A key with an instance under a perspective is left out - a quadratic seen in perspective is no longer one - and
    // walks its pieces instead.
    private bool BinCurveTiles(FlushRecord rec, Matrix4x4F projection)
    {
        var viewports = _device.CurrentViewports;
        if (SlotMatrix == null || viewports is not { Length: > 0 }) return false;

        _tileViewport = viewports[0];
        _tileProjection = projection;
        _tileCount = 0;
        _tilePieces = 0;
        _tileTotal = 0;
        _tileRows = 0;

        Fit(ref _solidTileBase, rec.Keys.Count);
        for (var i = 0; i < rec.Keys.Count; i++)
        {
            var (seg, first, n) = rec.Keys[i];
            _solidTileBase[i] = BeginTileKey(seg);
            for (var j = 0; j < n && _solidTileBase[i] >= 0; j++)
            {
                ref var it = ref seg.Items[first + j];
                if (!AddTileItem(seg, it.Local, (int)it.Params.X)) _solidTileBase[i] = DropTileKey();
            }
        }

        Fit(ref _gradTileBase, rec.GradKeys.Count);
        for (var i = 0; i < rec.GradKeys.Count; i++)
        {
            var (seg, first, n) = rec.GradKeys[i];
            _gradTileBase[i] = BeginTileKey(seg);
            for (var j = 0; j < n && _gradTileBase[i] >= 0; j++)
            {
                ref var it = ref seg.GradItems[first + j];
                if (!AddTileItem(seg, it.Local, (int)it.Geom1.W)) _gradTileBase[i] = DropTileKey();
            }
        }

        Fit(ref _patTileBase, rec.PatKeys.Count);
        for (var i = 0; i < rec.PatKeys.Count; i++)
        {
            var (seg, first, n, _) = rec.PatKeys[i];
            _patTileBase[i] = BeginTileKey(seg);
            for (var j = 0; j < n && _patTileBase[i] >= 0; j++)
            {
                ref var it = ref seg.PatItems[first + j];
                if (!AddTileItem(seg, it.Local, (int)it.Params.W)) _patTileBase[i] = DropTileKey();
            }
        }

        Fit(ref _texTileBase, rec.TexKeys.Count);
        for (var i = 0; i < rec.TexKeys.Count; i++)
        {
            var (seg, first, n, _, _) = rec.TexKeys[i];
            _texTileBase[i] = BeginTileKey(seg);
            for (var j = 0; j < n && _texTileBase[i] >= 0; j++)
            {
                ref var it = ref seg.TexItems[first + j];
                if (!AddTileItem(seg, it.Local, (int)it.Params.W)) _texTileBase[i] = DropTileKey();
            }
        }

        Fit(ref _matTileBase, rec.MatKeys.Count);
        for (var i = 0; i < rec.MatKeys.Count; i++)
        {
            var seg = rec.MatKeys[i].Seg;
            var first = rec.MatKeys[i].First;
            var n = rec.MatKeys[i].Count;
            _matTileBase[i] = BeginTileKey(seg);
            for (var j = 0; j < n && _matTileBase[i] >= 0; j++)
            {
                ref var it = ref seg.MatItems[first + j];
                if (!AddTileItem(seg, it.Local, (int)it.Params.W)) _matTileBase[i] = DropTileKey();
            }
        }

        if (_tileCount == 0) return false;

        _tileEffect ??= ToDispose(new CurveTileEffect(_device));
        rec.Tiles ??= new CurveTiler(_device, _tileEffect);
        var items = _tileItems.AsSpan(0, _tileCount);
        if (rec.Tiles.Holds(items, _tilePieces, _tileTotal, _tileRows)) return true;

        _device.SuspendRendering();
        rec.Tiles.Run(items, _tilePieces, _tileTotal, _tileRows);
        _device.ResumeRendering();
        return true;
    }

    // A key's first item among the flush's, or -1 for a key that draws no curves; the totals so far are kept so the key
    // can be dropped again.
    private int BeginTileKey(KeySegment seg)
    {
        if (seg.CurveWords == null) return -1;

        _keyTileCount = _tileCount;
        _keyTilePieces = _tilePieces;
        _keyTileTotal = _tileTotal;
        _keyTileRows = _tileRows;
        return _tileCount;
    }

    // Takes back the items of the key begun last; it walks its pieces instead.
    private int DropTileKey()
    {
        _tileCount = _keyTileCount;
        _tilePieces = _keyTilePieces;
        _tileTotal = _keyTileTotal;
        _tileRows = _keyTileRows;
        return -1;
    }

    private static void Fit(ref int[] bases, int count)
    {
        if (bases.Length < count) Array.Resize(ref bases, Math.Max(count, bases.Length * 2));
    }

    // One instance of a curve fill as the tiler reads it: its local space mapped to framebuffer pixels, and the tile grid
    // its box covers inside the viewport. False under a perspective.
    private bool AddTileItem(KeySegment seg, Matrix4x4F local, int slot)
    {
        var m = local * SlotMatrix(slot) * _tileProjection;
        if (Math.Abs(m.M14) > 1e-6f || Math.Abs(m.M24) > 1e-6f || Math.Abs(m.M44 - 1f) > 1e-6f) return false;

        var vp = _tileViewport;
        var sx = vp.Width * 0.5f;
        var sy = vp.Height * 0.5f;
        var words = seg.CurveWords;
        var item = new CurveTileItem
        {
            ToPixelX = new Vector4F(m.M11 * sx, m.M21 * sx, m.M41 * sx + vp.X + sx, 0f),
            ToPixelY = new Vector4F(m.M12 * sy, m.M22 * sy, m.M42 * sy + vp.Y + sy, 0f),
            Curves = seg.CurveBuffer.GetDeviceAddress(),
            PieceFirst = _tilePieces,
            PieceCount = words[4],
            EvenOdd = words[5],
            TileBase = _tileTotal,
            RowBase = _tileRows
        };

        // The fill's box in pixels, a margin wider, inside the viewport.
        var x0 = BitConverter.UInt32BitsToSingle(words[0]);
        var y0 = BitConverter.UInt32BitsToSingle(words[1]);
        var x1 = x0 + BitConverter.UInt32BitsToSingle(words[2]);
        var y1 = y0 + BitConverter.UInt32BitsToSingle(words[3]);
        var minX = float.MaxValue;
        var minY = float.MaxValue;
        var maxX = float.MinValue;
        var maxY = float.MinValue;
        for (var corner = 0; corner < 4; corner++)
        {
            var x = (corner & 1) == 0 ? x0 : x1;
            var y = (corner & 2) == 0 ? y0 : y1;
            var px = item.ToPixelX.X * x + item.ToPixelX.Y * y + item.ToPixelX.Z;
            var py = item.ToPixelY.X * x + item.ToPixelY.Y * y + item.ToPixelY.Z;
            minX = Math.Min(minX, px);
            minY = Math.Min(minY, py);
            maxX = Math.Max(maxX, px);
            maxY = Math.Max(maxY, py);
        }

        minX = Math.Max(minX - 1f, vp.X);
        maxX = Math.Min(maxX + 1f, vp.X + vp.Width - 0.001f);
        minY = Math.Max(minY - 1f, Math.Min(vp.Y, vp.Y + vp.Height));
        maxY = Math.Min(maxY + 1f, Math.Max(vp.Y, vp.Y + vp.Height) - 0.001f);
        if (minX <= maxX && minY <= maxY)
        {
            item.TileX = (int)MathF.Floor(minX / 16f);
            item.TileY = (int)MathF.Floor(minY / 16f);
            item.Columns = (uint)((int)MathF.Floor(maxX / 16f) - item.TileX + 1);
            item.Rows = (uint)((int)MathF.Floor(maxY / 16f) - item.TileY + 1);
        }

        _tileTotal += item.Columns * item.Rows;
        _tileRows += item.Rows;
        _tilePieces += item.PieceCount;
        if (_tileCount == _tileItems.Length) Array.Resize(ref _tileItems, _tileCount * 2);
        _tileItems[_tileCount++] = item;
        return true;
    }

    // Hands an effect's curve passes the flush's tiles, or tells them to walk each fill's pieces.
    private static void UseTiles(CurveTiler tiles, Effect effect, EffectParameter tiled)
    {
        tiled.SetValue(tiles != null ? 1u : 0u);
        tiles?.ApplyTo(effect);
    }

    // Whether a curve key reads the flush's tiles, from its first item on, or walks its pieces - a key the tiles left out.
    private static void TileKey(CurveTiler tiles, EffectParameter tiled, EffectParameter itemBase, int first)
    {
        var fromTiles = tiles != null && first >= 0;
        tiled.SetValue(fromTiles ? 1u : 0u);
        if (fromTiles) itemBase.SetValue((uint)first);
    }

    // A curve fill's instances, a quad each over the fill's box: no vertex buffer, the corners from the vertex index. The
    // pass and its effect's parameters are the brush's; the instance buffer is already set.
    private void DrawCurves(KeySegment seg, uint count, IEffectPass pass, EffectParameter curveData,
        EffectParameter viewportSize)
    {
        var viewports = _device.CurrentViewports;
        if (viewports is { Length: > 0 })
        {
            viewportSize.SetValue(new Vector2F(viewports[0].Width, viewports[0].Height));
        }

        _device.VertexType = null;
        _device.PrimitiveTopology = PrimitiveTopology.TriangleStrip;
        curveData.SetValue(seg.CurveBuffer.GetDeviceAddress());
        pass.Apply();
        _device.Draw(4, count, 0, 0);
    }

    private void SetupInstancedState(Matrix4x4F projection)
    {
        // Written every time - see SdfBatchCollector.DrawSegment for why a "same as last time" cache is not this
        // collector's to keep: an off-screen bake draws through the same effect with its own projection in between.
        _effect.Projection.SetValue(projection);
        _effect.Transforms.SetValue(TransformsAddress);
        if (_brush != null) _brush.Projection.SetValue(projection);
        if (_brush != null) _brush.Transforms.SetValue(TransformsAddress);
        var origin = TargetOrigin();
        _effect.TargetOrigin.SetValue(origin);
        _brush?.TargetOrigin.SetValue(origin);
        _material?.TargetOrigin.SetValue(origin);

        _device.VertexType = typeof(UIVertex);
        _device.PolygonMode = PolygonMode.Fill;
        _device.RasterizerDiscardEnabled = false;   // a prior compute pass (fringe/stroke expander) may have left discard ON
        _device.CullMode = CullModeFlagBits.None;    // 2D fills: never cull (tessellated winding is arbitrary)
        _device.ColorBlendEnabled = true;
        _device.ColorBlendEquation = ColorBlendEquations.AlphaBlend;
        _device.DepthTestEnabled = true;
        _device.DepthWriteEnable = true;
        _device.DepthCompareFunction = CompareOp.Always;
    }

    // The mesh pass for one procedural kind. Mirrors PatternRectCollector.DrawPass, which does the same for the analytic
    // shapes - the numbers are PatternType / NoiseType as baked into Params.Y, and an unknown one draws as checkerboard
    // rather than not at all.
    private static IEffectPass MeshPassFor(BrushEffect brush, int kind) => kind switch
    {
        1 => brush.PatternStripesMeshPass,
        2 => brush.PatternDotsMeshPass,
        3 => brush.PatternGridMeshPass,
        4 => brush.PatternHexagonMeshPass,
        5 => brush.PatternHatchMeshPass,
        6 => brush.PatternWeaveMeshPass,

        100 => brush.NoiseSimplexMeshPass,
        101 => brush.NoisePerlinMeshPass,
        102 => brush.NoiseValueMeshPass,
        103 => brush.NoiseWorleyMeshPass,
        104 => brush.NoiseRidgedMeshPass,
        105 => brush.NoiseTurbulenceMeshPass,
        106 => brush.NoiseVoronoiMeshPass,
        107 => brush.NoiseCombustibleMeshPass,
        108 => brush.NoiseCircuitMeshPass,
        _ => brush.PatternCheckerboardMeshPass
    };

    // The curve pass for one procedural kind, numbered as MeshPassFor.
    private static IEffectPass CurvePassFor(BrushEffect brush, int kind) => kind switch
    {
        1 => brush.PatternStripesCurvePass,
        2 => brush.PatternDotsCurvePass,
        3 => brush.PatternGridCurvePass,
        4 => brush.PatternHexagonCurvePass,
        5 => brush.PatternHatchCurvePass,
        6 => brush.PatternWeaveCurvePass,

        100 => brush.NoiseSimplexCurvePass,
        101 => brush.NoisePerlinCurvePass,
        102 => brush.NoiseValueCurvePass,
        103 => brush.NoiseWorleyCurvePass,
        104 => brush.NoiseRidgedCurvePass,
        105 => brush.NoiseTurbulenceCurvePass,
        106 => brush.NoiseVoronoiCurvePass,
        107 => brush.NoiseCombustibleCurvePass,
        108 => brush.NoiseCircuitCurvePass,
        _ => brush.PatternCheckerboardCurvePass
    };

    // Fringe device state: as the fill, but no vertex buffer (the ring is read by address) and the pixel basis its VS
    // offsets in.
    private void SetupFringeState(Matrix4x4F projection)
    {
        SetupInstancedState(projection);
        _device.VertexType = null;
        _device.PrimitiveTopology = PrimitiveTopology.TriangleList;
        var vp = _device.CurrentViewports;
        if (vp is { Length: > 0 })
        {
            _effect.ViewportSize.SetValue(new Vector2F(vp[0].Width, vp[0].Height));
            if (_brush != null) _brush.ViewportSize.SetValue(new Vector2F(vp[0].Width, vp[0].Height));
        }
        _effect.FringePixels.SetValue(DeviceFringePx);
        if (_brush != null) _brush.FringePixels.SetValue(DeviceFringePx);
    }

    // Fringe width in DEVICE pixels - the same constant the per-unit fringe uses (GpuFillRenderComponent).
    private const float DeviceFringePx = 1.0f;

    // Build (once) the immutable vtx/idx buffers for a key's shared local mesh. Returns null until it has a drawable mesh.
    private KeySegment GetOrCreate(GeometryKey key, FrozenMesh mesh)
    {
        // The clock, not the walk's start: a patch frame asks for keys between walks too.
        var now = Clock();
        if (_keys.TryGetValue(key, out var seg) && seg.MeshUploaded)
        {
            seg.LastUsed = now;
            return seg;
        }

        // A curve fill holds its packed curves and nothing else: no mesh, no ring.
        if (key.Kind == GeometryKind.Curves)
        {
            if (mesh.Curves is not { } curves) return null;

            seg ??= new KeySegment();
            seg.CurveWords = curves.Words;
            seg.CurveBuffer = Upload(curves.Words, BufferUsageFlags.StorageBuffer | BufferUsageFlags.ShaderDeviceAddress,
                "shared curve fill");
            seg.MeshUploaded = true;
            seg.LastUsed = now;
            seg.MeshBytes = (long)seg.CurveBuffer.DeviceMemoryBytes;
            _meshBytes += seg.MeshBytes;
            _keys[key] = seg;
            return seg;
        }

        var vertices = mesh.Vertices;
        if (vertices.Length == 0) return null;
        var indices = mesh.Indices;
        var indexed = indices is { Length: > 0 };

        seg ??= new KeySegment();
        seg.VtxBuffer = Upload(vertices, BufferUsageFlags.VertexBuffer, "shared mesh vertices");
        seg.VertexCount = (uint)vertices.Length;

        seg.Indexed = indexed;
        if (indexed)
        {
            seg.IdxBuffer = Upload(indices, BufferUsageFlags.IndexBuffer, "shared mesh indices");
            seg.IndexCount = (uint)indices.Length;
        }

        // The fringe ring: immutable like the mesh (both are keyed on the same tessellation).
        if (mesh.Ring is { Length: > 0 } ring)
        {
            seg.RingBuffer = Upload(ring, BufferUsageFlags.StorageBuffer | BufferUsageFlags.ShaderDeviceAddress,
                "shared mesh fringe");
            seg.RingVertexCount = (uint)ring.Length * 6;
        }

        seg.Topology = mesh.Topology;
        seg.MeshUploaded = true;
        seg.LastUsed = now;
        seg.MeshBytes = (long)((seg.VtxBuffer?.DeviceMemoryBytes ?? 0) + (seg.IdxBuffer?.DeviceMemoryBytes ?? 0) +
                               (seg.RingBuffer?.DeviceMemoryBytes ?? 0));
        _meshBytes += seg.MeshBytes;
        _keys[key] = seg;
        return seg;
    }

    private Buffer Upload<T>(T[] data, BufferUsageFlags usage, string name) where T : unmanaged
    {
        var buffer = Buffer.New<T>(_device, (uint)data.Length, usage, Mem);
        buffer.Name = name;
        buffer.SetData(data, 0, (uint)data.Length);
        return buffer;
    }

    // ---- Retained ARENA surface (see InstancedKeyArena) -------------------------------------------------------------
    // A key's instances are one append-only array and a flush record keeps a contiguous run of it under one clip - which
    // is a segment, said in this collector's own words. Everything below is that translation and nothing more; the flush,
    // its coverage mark and the deferred overlays are untouched.

    /// <summary>The arena and slot the last accepted SOLID instance went to - read by the walk right after TryAdd, the
    /// same way the SDF batches expose LastSlot.</summary>
    public BatchArena LastArena { get; private set; }
    public int LastSlot { get; private set; }

    /// <summary>Which RenderOp.Batch an instanced flush is recorded under.</summary>
    public const byte ArenaBatchId = 17;

    private readonly Dictionary<GeometryKey, InstancedKeyArena> _arenas = new();

    /// <summary>The arena for the key this unit's SOLID fill would join, or null when it has none (another family, no
    /// drawable mesh). One arena per key: a slot number only means something inside its own array.</summary>
    public BatchArena ArenaFor(GeometryRenderUnit unit)
    {
        if (!unit.TryGetInstancedFill(out var key, out var meshObj, out _)) return null;
        if (meshObj is not FrozenMesh mesh) return null;
        var seg = GetOrCreate(key, mesh);
        if (seg == null) return null;

        if (!_arenas.TryGetValue(key, out var arena)) _arenas[key] = arena = new InstancedKeyArena(this, seg);
        return arena;
    }

    internal int KeyCount(object key) => key is KeySegment seg ? seg.Count : 0;

    /// <summary>The segment cached for <paramref name="key"/>, as the arena surface names it; null when none is.</summary>
    internal object SegmentOf(GeometryKey key) => _keys.GetValueOrDefault(key);

    internal int KeyCapacityLeft(object key) => key is KeySegment seg ? seg.GpuCapacity - seg.Count : 0;

    /// <summary>The run this key draws in that flush record, or (-1, -1) when the record does not draw this key at all.</summary>
    internal (int First, int Count) KeyRange(object key, int flush)
    {
        if (key is not KeySegment seg || flush < 0 || flush >= _flushCount) return (-1, -1);

        var rec = _flushRecords[flush];
        foreach (var entry in rec.Keys)
        {
            if (ReferenceEquals(entry.Seg, seg)) return ((int)entry.First, (int)entry.Count);
        }

        return (-1, -1);
    }

    /// <summary>Which recorded flush draws the slot this key holds - the way back from a group's run to its segment.
    /// Only the LIVE records are asked: a leftover from a longer frame still holds the ranges it had then, and answering
    /// with one of those named a flush no recorded op draws - which is how a hovered close button refused its patch 151
    /// times in ten seconds and took a walk of the window each time.</summary>
    internal int KeyFlushContaining(object key, int slot)
    {
        if (key is not KeySegment seg) return -1;

        for (var i = 0; i < _flushCount; i++)
        {
            foreach (var entry in _flushRecords[i].Keys)
            {
                if (ReferenceEquals(entry.Seg, seg) && slot >= entry.First && slot < entry.First + entry.Count) return i;
            }
        }

        return -1;
    }

    internal Rect2D FlushScissor(int flush) => flush >= 0 && flush < _flushCount ? _flushRecords[flush].Scissor : null;

    /// <summary>Take this component's deferred fringe/stroke out of every LIVE record. The other half of "it stopped
    /// drawing": its shape rides the instance buffer and blanks with it, but its ink is a per-unit draw the record holds
    /// by reference, and a stroked path is nothing BUT ink - a tab's close cross left a bare stroke hanging on the frame
    /// with no shape under it, and it only went away when the next full walk happened to re-record.</summary>
    internal void DropUnitsOf(IUIComponent component)
    {
        if (component == null) return;

        for (var i = 0; i < _flushCount; i++)
        {
            var units = _flushRecords[i].Units;
            for (var k = units.Count - 1; k >= 0; k--)
                if (ReferenceEquals(units[k].Component, component)) units.RemoveAt(k);
        }
    }

    /// <summary>Make this key's slots draw nothing, in place. A zeroed instance carries a zero transform slot and a zero
    /// color, so it covers no pixel - the same answer BlankRun gives every other family.</summary>
    internal void BlankKeySlots(object key, int first, int count)
    {
        if (key is not KeySegment seg || first < 0 || count <= 0 || first + count > seg.Count) return;

        PrepareRetainedWrite();
        for (var i = 0; i < count; i++) seg.Items[first + i] = default;
        seg.Gpu?.SetData(seg.Items.AsSpan(first, count), (uint)(first * InstanceStride));
    }

    /// <summary>Bake one unit's SOLID instance into the patch stage, for THIS key only - a unit whose shape hashes to a
    /// different mesh belongs to another arena and must not be written into this one's array.</summary>
    internal bool TryStageSolid(object key, IRenderUnit unit, Matrix4x4F world, int transformSlot, List<GeometryInstance> stage,
        int clipSlot = -1)
    {
        if (key is not KeySegment seg) return false;
        if (unit is not GeometryRenderUnit gru) return false;
        if (!gru.TryGetInstancedFill(out var unitKey, out var meshObj, out var color)) return false;
        if (meshObj is not FrozenMesh mesh || !ReferenceEquals(GetOrCreate(unitKey, mesh), seg)) return false;

        stage.Add(GeometryInstance.FromLocal(gru.PlaceBaked(world), color, transformSlot, FadeSlotFor(gru), clipSlot));
        return true;
    }

    /// <summary>The opacity slot this instance reads. Every unit reads one now, per-unit overlay or not: an overlay is
    /// re-issued by the CPU on each frame and is handed the current alpha before it draws (RenderCache's
    /// RefreshOverlayFade), so the chain no longer has to sit in the baked color to reach it.</summary>
    private static int FadeSlotFor(GeometryRenderUnit unit) => unit.FadeSlot;

    /// <summary>Replace [at, at+replaced) of this key's run in that flush with a staged range: the tail of the key's own
    /// array shifts, and every LATER run of the same key moves with it. Nothing else in the collector is touched, and the
    /// op that draws the flush stays exactly where it stands - so paint order holds by construction.</summary>
    internal bool ReplaceInKey(object key, int flush, int at, int replaced, List<GeometryInstance> stage, int stageFirst, int stageCount)
    {
        if (key is not KeySegment seg || flush < 0 || flush >= _flushCount) return false;
        if (seg.Gpu == null) return false;

        PrepareRetainedWrite();
        var rec = _flushRecords[flush];
        var index = rec.Keys.FindIndex(k => ReferenceEquals(k.Seg, seg));
        if (index < 0) return false;

        var entry = rec.Keys[index];
        var first = (int)entry.First;
        var count = (int)entry.Count;
        if (at < 0 || replaced < 0 || at + replaced > count) return false;

        var delta = stageCount - replaced;
        if (seg.Count + delta > seg.GpuCapacity) return false;   // no room in this key's buffer -> the walk compacts it

        var editAt = first + at;
        var tailAt = editAt + replaced;
        var tailLen = seg.Count - tailAt;
        if (delta != 0 && tailLen > 0) Array.Copy(seg.Items, tailAt, seg.Items, tailAt + delta, tailLen);
        for (var i = 0; i < stageCount; i++) seg.Items[editAt + i] = stage[stageFirst + i];

        seg.Count += delta;
        seg.Flushed = seg.Count;
        rec.Keys[index] = (seg, entry.First, (uint)(count + delta));

        // Every later run of THIS key shifted by the same amount - in this record and in the LIVE ones after it.
        for (var f = flush; f < _flushCount; f++)
        {
            var other = _flushRecords[f];
            for (var k = 0; k < other.Keys.Count; k++)
            {
                var later = other.Keys[k];
                if (!ReferenceEquals(later.Seg, seg) || later.First <= entry.First) continue;
                other.Keys[k] = (later.Seg, (uint)(later.First + delta), later.Count);
            }
        }

        var touched = Math.Min(stageCount + (delta != 0 ? tailLen + Math.Max(0, -delta) : 0), seg.Count - editAt);
        if (touched > 0) seg.Gpu.SetData(seg.Items.AsSpan(editAt, touched), (uint)(editAt * InstanceStride));
        return true;
    }

    /// <summary>Give a staged run a flush of its OWN - a control that drew no vector fill until now. A new record rather
    /// than a new entry in an existing one: a record carries ONE coverage mark and one clip, and joining a stranger's
    /// group would put this shape's fringe under a mark that is not its own.</summary>
    internal int AllocateFlushForKey(object key, Rect2D scissor, List<GeometryInstance> stage, int stageFirst, int stageCount)
    {
        if (key is not KeySegment seg || stageCount <= 0) return -1;

        // A key that has never drawn has no instance buffer yet - the walk builds it lazily on the first add, and a patch
        // that places the FIRST shape of a mesh is exactly that case. Same lazy build, same reason (a whole ring, not one
        // buffer, or this key spends its life writing a copy the frames in flight are still reading).
        PrepareRetainedWrite();
        EnsureInstanceRing(seg);
        if (seg.Gpu == null) return -1;
        while (seg.Count + stageCount > seg.Items.Length) Array.Resize(ref seg.Items, seg.Items.Length * 2);
        if (seg.Count + stageCount > seg.GpuCapacity) return -1;

        var first = seg.Count;
        for (var i = 0; i < stageCount; i++) seg.Items[first + i] = stage[stageFirst + i];
        seg.Count += stageCount;
        seg.Flushed = seg.Count;
        seg.Gpu.SetData(seg.Items.AsSpan(first, stageCount), (uint)(first * InstanceStride));

        var rec = TakeFlushRecord();
        rec.Scissor = scissor;
        rec.StencilRef = Math.Min(++_groupRef, CoverageMarkBits);
        rec.Keys.Add((seg, (uint)first, (uint)stageCount));
        return _flushCount - 1;
    }

    // The lazy instance ring, shared by the walk (TryAdd) and by a patch placing a brand-new shape.
    private void EnsureInstanceRing(KeySegment seg)
    {
        if (seg.Gpu != null || !seg.MeshUploaded) return;

        var copies = (int)Math.Max(1u, _device.MaxFramesInFlight);
        if (seg.GpuRing != null) DeferRing(seg.GpuRing);
        seg.GpuRing = new Buffer[copies];
        for (var i = 0; i < copies; i++)
        {
            seg.GpuRing[i] = Buffer.New<GeometryInstance>(_device, (uint)seg.Items.Length,
                BufferUsageFlags.StorageBuffer | BufferUsageFlags.ShaderDeviceAddress, Mem);
        }

        seg.Gpu = seg.GpuRing[_copy % copies];
        seg.GpuCapacity = seg.Items.Length;
        seg.Recreated = true;
    }

    internal void UpdateKeySlot(object key, int slot, GeometryInstance item)
    {
        if (key is not KeySegment seg || seg.Gpu == null || slot < 0 || slot >= seg.Count) return;

        PrepareRetainedWrite();
        seg.Items[slot] = item;
        seg.Gpu.SetData(seg.Items.AsSpan(slot, 1), (uint)(slot * InstanceStride));
    }
}

