using System;
using System.Collections.Generic;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.EffectsFramework;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.FX;
using Adamantium.Vulkan.Core;
using Buffer = Adamantium.Graphics.Buffer;

namespace Adamantium.UI.Rendering.RenderUnits;

/// <summary>
/// GPU strokes. A continuous stroke - and one dashed with flat ends, as a mask along its length - keeps only its points:
/// the vertex shader works out its ribbon where it is drawn, at any width. Dashes with shaped ends and trims are cut
/// into pieces by a compute pass, drawn with an indirect count. Buffers are rented, so same-topology changes rewrite in
/// place.
/// </summary>
public sealed class GpuStrokeRenderComponent : UIRenderComponent
{
    // Disc-fan subdivision for round joins (a smooth-enough circle); 0 when the contour has no round geometry.
    private const uint RoundSegmentsCount = 16;
    private const int MaxDashVertices = 8192 * 6;   // safety cap on the dash output buffer (per contour)
    private const ulong MostBatchPoints = PreparedStrokeBatch.MostPoints;
    private const ulong MostBatchVertexBytes = 32UL << 20;
    private static readonly ulong VertexSize = (ulong)System.Runtime.CompilerServices.Unsafe.SizeOf<StrokeVertex>();

    private const ColorComponentFlagBits AllColorComponents = ColorComponentFlagBits.RBit |
                                                              ColorComponentFlagBits.GBit |
                                                              ColorComponentFlagBits.BBit |
                                                              ColorComponentFlagBits.ABit;


    private const MemoryPropertyFlags StrokeMemory = MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.DeviceLocal;
    private const MemoryPropertyFlags VertexMemory = MemoryPropertyFlags.DeviceLocal;

    private readonly IGraphicsDevice _device;
    private readonly StrokeEffect _effect;
    private Pen _pen;                        // mutable: TryRepoint swaps it for a uniform-only pen change (animation)
    private readonly bool _useCut;          // dash and/or trim -> the single-thread DashCut kernel + DrawIndirect
    private readonly bool _hasDashes;
    private readonly uint _joinType;        // 0 miter, 1 bevel, 2 round
    private readonly List<Contour> _contours = [];

    // A cut is kept until what it was cut from changes.
    private float _expandedHalf = float.NaN;
    private float _expandedFringe = float.NaN;
    private (double Offset, double Phase, double TrimStart, double TrimEnd, PenLineCap Start, PenLineCap End,
        PenLineCap DashStart, PenLineCap DashEnd) _expandedCut;
    private bool _cutDirty = true;

    // Per-contour GPU state. The expander runs once per contour (its own points + output buffers), so combined/group
    // geometry strokes every contour instead of falling back to the CPU path.
    private sealed class Contour
    {
        public bool IsClosed;
        public uint RoundSegments;   // the triangles a point keeps for its join - depends on the join only (caps are analytic)
        public uint PointCount;
        public uint ThreadCount;     // cut threads: contours of a batch, or 1
        public uint VertexCount;     // continuous: exact draw count; cut: output capacity (actual count is on GPU)
        public float[] PointsData;   // x,y interleaved - re-uploaded to the current ring slot when stale
        public ReusableBuffer PointsBuffer;
        public ReusableBuffer VertexBuffer;   // the cut's output; none for a continuous stroke
        public float[] ArcsData;              // continuous: each point's distance along its contour
        public ReusableBuffer ArcsBuffer;
        public float[] PatternData;           // dashes as read, stretched by DashScale
        public ReusableBuffer PatternBuffer;
        public Buffer IndirectBuffer;    // dash only (VkDrawIndirectCommand)
        public float DashScale = 1f;     // what FitDashesToContour stretched the pattern by (1 = untouched)

        // A BATCH: many contours, their points one after another, drawn by one draw. ContoursData is (first point, point
        // count, closed) per contour; PointContoursData the contour of each point.
        public bool Batched;
        public bool LaidOutElsewhere;   // its arrays are a PreparedStrokeBatch's: shared, so copied before written
        public uint[] ContoursData;
        public uint[] PointContoursData;
        public ReusableBuffer ContoursBuffer;
        public ReusableBuffer PointContoursBuffer;

        // A batch of CUT contours: per contour, the first vertex of its output range and how long the range is.
        public uint[] SlotsData;
        public ReusableBuffer SlotsBuffer;

        // Cut only: each contour's arc length and segments, and the output room it was given.
        public (double Length, int Segments)[] Arcs;
        public uint[] Room;

        public ulong PointsBytes => (ulong)(PointsData.Length * sizeof(float));
        public ulong VertexBytes => (ulong)VertexCount * VertexSize;
    }

    public GpuStrokeRenderComponent(IGraphicsDevice device, UIBasicEffect uiBasicEffect, StrokeEffect effect,
        IReadOnlyList<(Vector2<double>[] Points, bool IsClosed)> contours, Pen pen, GpuBufferManager bufferManager)
        : this(device, uiBasicEffect, effect, contours, null, pen, bufferManager)
    {
    }

    /// <summary>A stroke along <paramref name="contours"/>, its continuous batches taken from
    /// <paramref name="laidOut"/> - what <see cref="PreparedStrokeBatch.Of"/> made of these same contours - rather than
    /// laid out here.</summary>
    internal GpuStrokeRenderComponent(IGraphicsDevice device, UIBasicEffect uiBasicEffect, StrokeEffect effect,
        IReadOnlyList<(Vector2<double>[] Points, bool IsClosed)> contours, PreparedStrokeBatch[] laidOut, Pen pen,
        GpuBufferManager bufferManager) : base(device, uiBasicEffect, null, bufferManager)
    {
        _device = device;
        _effect = effect;
        _pen = pen;
        _hasDashes = pen.Dashes is { Count: > 0 };
        _useCut = UsesCut(pen);
        _joinType = MapJoin(pen.PenLineJoin);

        // Strokes of many contours - a map's roads, a chart's series, a map's dashed paths - go as ONE batch: a dispatch
        // and a draw per contour is what thousands of them cannot afford. Dashed or trimmed, each contour of a batch is
        // still walked by its own arc length, by a thread of its own; one whose dashes are fitted to it keeps a draw of
        // its own, as its pattern is its own.
        var valid = Valid(contours);
        IReadOnlyList<(Vector2<double>[] Points, bool IsClosed)> alone = [];
        var together = valid;
        if (_useCut && _pen.FitDashesToContour)
        {
            var fitted = new List<(Vector2<double>[] Points, bool IsClosed)>();
            var open = new List<(Vector2<double>[] Points, bool IsClosed)>();
            foreach (var contour in valid) (contour.IsClosed ? fitted : open).Add(contour);
            alone = fitted;
            together = fitted.Count > 0 ? open : valid;
        }

        if (together.Count > 1)
        {
            var runs = _useCut ? CutBatches(together) : Batches(together);
            var reuse = !_useCut && laidOut != null && laidOut.Length == runs.Count;
            for (var r = 0; r < runs.Count; r++)
            {
                var (start, count) = runs[r];
                var contour = count == 1 ? BuildContour(together[start].Points, together[start].IsClosed)
                    : _useCut ? BuildCutBatch(together, start, count)
                    : reuse && laidOut[r] is { } batch && batch.Start == start && batch.Count == count ? BuildBatch(batch)
                    : BuildBatch(together, start, count);
                if (contour != null) _contours.Add(contour);
            }

            valid = alone;
        }

        foreach (var (points, isClosed) in valid)
        {
            var contour = BuildContour(points, isClosed);
            if (contour != null) _contours.Add(contour);
        }
    }

    /// <summary>How many draws the stroke makes: one per batch, one per contour that stands alone.</summary>
    internal int DrawCount => _contours.Count;

    /// <summary>Whether draw <paramref name="draw"/> is a batch of contours.</summary>
    internal bool IsBatch(int draw) => _contours[draw].Batched;

    private static IReadOnlyList<(Vector2<double>[] Points, bool IsClosed)> Valid(
        IReadOnlyList<(Vector2<double>[] Points, bool IsClosed)> contours) => PreparedStrokeBatch.Valid(contours);

    private static List<(int Start, int Count)> Batches(IReadOnlyList<(Vector2<double>[] Points, bool IsClosed)> contours) =>
        PreparedStrokeBatch.Batches(contours);

    // Cut contours grouped likewise, their output ranges summing to no more than MostBatchVertexBytes.
    private List<(int Start, int Count)> CutBatches(IReadOnlyList<(Vector2<double>[] Points, bool IsClosed)> contours)
    {
        var mostVertices = MostBatchVertexBytes / VertexSize;
        var batches = new List<(int Start, int Count)>();
        var start = 0;
        var points = 0UL;
        var vertices = 0UL;
        for (var i = 0; i < contours.Count; i++)
        {
            var capacity = CutCapacity(ArcOf(contours[i].Points, contours[i].IsClosed), _pen, CutRoundSegments, DashRoom);
            if (i > start &&
                (points + (ulong)contours[i].Points.Length > MostBatchPoints || vertices + capacity > mostVertices))
            {
                batches.Add((start, i - start));
                start = i;
                points = 0;
                vertices = 0;
            }

            points += (ulong)contours[i].Points.Length;
            vertices += capacity;
        }

        if (contours.Count > start) batches.Add((start, contours.Count - start));
        return batches;
    }

    private uint CutRoundSegments => _joinType == 2u ? RoundSegmentsCount : 0u;

    // The triangles a point of a continuous stroke draws for its join.
    private uint ExpandJoinTriangles => _joinType switch
    {
        2u => RoundSegmentsCount,
        1u => 2u,
        _ => 0u,
    };

    // A batch of dashed or trimmed contours: their points and table as a continuous batch has them, and for each contour
    // the range of the output its cut writes - as long as the most its cut can write.
    private Contour BuildCutBatch(IReadOnlyList<(Vector2<double>[] Points, bool IsClosed)> contours, int start, int count)
    {
        var total = 0;
        for (var k = start; k < start + count; k++) total += contours[k].Points.Length;

        var c = new Contour
        {
            Batched = true,
            PointCount = (uint)total,
            PointsData = new float[total * 2],
            ContoursData = new uint[count * 3],
            PointContoursData = new uint[total],
            SlotsData = new uint[count * 2],
            RoundSegments = CutRoundSegments,
            ThreadCount = (uint)count,
        };
        FillBatch(c, contours, start, count);

        var at = 0u;
        c.Arcs = new (double, int)[count];
        c.Room = new uint[count];
        for (var k = 0; k < count; k++)
        {
            c.Arcs[k] = ArcOf(contours[start + k].Points, contours[start + k].IsClosed);
            var capacity = CutCapacity(c.Arcs[k], _pen, c.RoundSegments, DashRoom);
            c.SlotsData[k * 2] = at;
            c.SlotsData[k * 2 + 1] = capacity;
            c.Room[k] = capacity;
            at += capacity;
        }

        c.VertexCount = at;

        c.PointsBuffer = NewStorage(c.PointsBytes);
        c.ContoursBuffer = NewStorage((ulong)(c.ContoursData.Length * sizeof(uint)));
        c.PointContoursBuffer = NewStorage((ulong)(c.PointContoursData.Length * sizeof(uint)));
        c.SlotsBuffer = NewStorage((ulong)(c.SlotsData.Length * sizeof(uint)));
        SetPattern(c, _pen);
        c.VertexBuffer = NewVertices(c.VertexBytes);
        return c;
    }

    // One contour standing for all of them: their points one after another, a table of where each starts, and for every
    // point the contour it is in. Slots in the output follow the points, so contours never write over each other.
    private Contour BuildBatch(PreparedStrokeBatch batch)
    {
        var c = new Contour
        {
            Batched = true,
            LaidOutElsewhere = true,
            PointCount = (uint)batch.PointContours.Length,
            PointsData = batch.Points,
            ContoursData = batch.Contours,
            PointContoursData = batch.PointContours,
            ArcsData = batch.Arcs,
        };

        c.RoundSegments = ExpandJoinTriangles;
        c.ThreadCount = c.PointCount;
        c.VertexCount = c.PointCount * (6u + c.RoundSegments * 3u);
        c.PointsBuffer = NewStorage(c.PointsBytes);
        c.ContoursBuffer = NewStorage((ulong)(c.ContoursData.Length * sizeof(uint)));
        c.PointContoursBuffer = NewStorage((ulong)(c.PointContoursData.Length * sizeof(uint)));
        c.ArcsBuffer = NewStorage((ulong)(c.ArcsData.Length * sizeof(float)));
        if (_hasDashes) SetPattern(c, _pen);
        return c;
    }

    private Contour BuildBatch(IReadOnlyList<(Vector2<double>[] Points, bool IsClosed)> contours, int start, int count)
    {
        var total = 0;
        for (var k = start; k < start + count; k++) total += contours[k].Points.Length;

        var c = new Contour
        {
            Batched = true,
            PointCount = (uint)total,
            PointsData = new float[total * 2],
            ContoursData = new uint[count * 3],
            PointContoursData = new uint[total],
        };
        FillBatch(c, contours, start, count);

        c.RoundSegments = ExpandJoinTriangles;
        c.ThreadCount = c.PointCount;
        c.VertexCount = c.PointCount * (6u + c.RoundSegments * 3u);
        c.PointsBuffer = NewStorage(c.PointsBytes);
        c.ContoursBuffer = NewStorage((ulong)(c.ContoursData.Length * sizeof(uint)));
        c.PointContoursBuffer = NewStorage((ulong)(c.PointContoursData.Length * sizeof(uint)));
        c.ArcsData = new float[total];
        FillArcs(c);
        c.ArcsBuffer = NewStorage((ulong)(total * sizeof(float)));
        if (_hasDashes) SetPattern(c, _pen);
        return c;
    }

    private static void FillArcs(Contour c)
    {
        var points = c.PointsData;
        var arcs = c.ArcsData;
        if (!c.Batched)
        {
            Arcs(points, arcs, 0, (int)c.PointCount);
            return;
        }

        for (var k = 0; k < c.ContoursData.Length; k += 3)
        {
            Arcs(points, arcs, (int)c.ContoursData[k], (int)c.ContoursData[k + 1]);
        }

        static void Arcs(float[] points, float[] arcs, int first, int count)
        {
            var along = 0.0;
            arcs[first] = 0f;
            for (var i = first + 1; i < first + count; i++)
            {
                var dx = points[i * 2] - points[i * 2 - 2];
                var dy = points[i * 2 + 1] - points[i * 2 - 1];
                along += Math.Sqrt((double)dx * dx + (double)dy * dy);
                arcs[i] = (float)along;
            }
        }
    }

    // Whether contour or batch c holds exactly these contours - counts and closedness, in order.
    private static bool Matches(Contour c, IReadOnlyList<(Vector2<double>[] Points, bool IsClosed)> contours, int start,
        int count)
    {
        if (!c.Batched)
            return count == 1 && contours[start].Points.Length == c.PointCount && contours[start].IsClosed == c.IsClosed;

        if (count * 3 != c.ContoursData.Length) return false;
        var at = 0u;
        for (var k = 0; k < count; k++)
        {
            var (points, isClosed) = contours[start + k];
            if (c.ContoursData[k * 3] != at || c.ContoursData[k * 3 + 1] != (uint)points.Length ||
                c.ContoursData[k * 3 + 2] != (isClosed ? 1u : 0u))
                return false;
            at += (uint)points.Length;
        }

        return true;
    }

    private static void FillBatch(Contour c, IReadOnlyList<(Vector2<double>[] Points, bool IsClosed)> contours, int start,
        int count)
    {
        var at = 0;
        for (var k = 0; k < count; k++)
        {
            var (points, isClosed) = contours[start + k];
            c.ContoursData[k * 3] = (uint)at;
            c.ContoursData[k * 3 + 1] = (uint)points.Length;
            c.ContoursData[k * 3 + 2] = isClosed ? 1u : 0u;
            foreach (var point in points)
            {
                c.PointsData[at * 2] = (float)point.X;
                c.PointsData[at * 2 + 1] = (float)point.Y;
                c.PointContoursData[at] = (uint)k;
                at++;
            }
        }
    }

    private ReusableBuffer NewStorage(ulong bytes)
    {
        var buffer = ToDispose(BufferManager.CreateBuffer(
            BufferUsageFlags.StorageBuffer | BufferUsageFlags.ShaderDeviceAddress, StrokeMemory, "stroke data"));
        buffer.Reserve(bytes);
        buffer.Invalidate();
        return buffer;
    }

    // A vertex as StrokeVertex lays it out.
    private ReusableBuffer NewVertices(ulong bytes)
    {
        var buffer = ToDispose(BufferManager.CreateBuffer(
            BufferUsageFlags.VertexBuffer | BufferUsageFlags.StorageBuffer | BufferUsageFlags.ShaderDeviceAddress,
            VertexMemory, "stroke vertices"));
        buffer.Reserve(bytes);
        buffer.Invalidate();
        return buffer;
    }

    // True when newPen keeps every SIZE-affecting parameter (cut mode, join, dash pattern -> same buffer sizes + fan
    // counts). The rest (thickness, dash offset, trim values, color, and now every CAP - they are analytic, so they
    // emit no geometry at all) are GPU uniforms, so a compatible pen can be swapped without reallocating - TryRepoint /
    // TryUpdateGeometry just re-dispatch with the new uniforms.
    private bool IsPenSizeCompatible(Pen newPen)
    {
        if (newPen?.Brush is not SolidColorBrush) return false;   // null pen (no stroke, e.g. thickness 0) or non-solid -> not repointable
        if ((newPen.Dashes is { Count: > 0 }) != _hasDashes) return false;   // a dash mask has a pattern to read
        var newUseCut = UsesCut(newPen);
        if (newUseCut != _useCut) return false;                       // continuous <-> cut changes the buffer set
        if (MapJoin(newPen.PenLineJoin) != _joinType) return false;  // join changes the corner fan size
        if (newPen.FitDashesToContour != _pen.FitDashesToContour) return false;   // fitting decides what is batched

        // Dash pattern (buffer size + content): each GetPen() makes a NEW collection, so compare by value, not ref.
        var oldDash = _pen.Dashes; var newDash = newPen.Dashes;
        var oldN = oldDash?.Count ?? 0; var newN = newDash?.Count ?? 0;
        if (oldN != newN) return false;
        if (!PatternChanged(newPen)) return true;

        // Another pattern of as many entries: a mask reads it; a cut one has to fit the room it was given.
        if (!_useCut) return true;
        if (_pen.FitDashesToContour) return false;
        foreach (var c in _contours)
        {
            for (var k = 0; k < c.Arcs.Length; k++)
            {
                if (CutCapacity(c.Arcs[k], newPen, c.RoundSegments, 1) > c.Room[k]) return false;
            }
        }

        return true;
    }

    // A trim, or dashes with shaped ends or fitted to the contour, are cut; flat-ended dashes are a mask.
    private static bool UsesCut(Pen pen)
    {
        if (pen.TrimStart > 0.0 || pen.TrimEnd < 1.0) return true;
        if (pen.Dashes is not { Count: > 0 }) return false;
        return pen.FitDashesToContour || pen.DashStartCap != PenLineCap.Flat || pen.DashEndCap != PenLineCap.Flat;
    }

    private bool PatternChanged(Pen newPen)
    {
        var oldDash = _pen.Dashes;
        var newDash = newPen.Dashes;
        for (var i = 0; i < (oldDash?.Count ?? 0); i++) if (oldDash[i] != newDash[i]) return true;
        return false;
    }


    // Cheap pen update for animation: a size-compatible pen change (thickness/offset/trim/color) keeps the buffers, so
    // just swap the pen and re-dispatch with the new uniforms - no per-frame realloc/re-upload (that churn is what tanks
    // FPS during stroke animation). Returns false (caller rebuilds via ProcessStrokeData) when a size-affecting param
    // changed.
    public bool TryRepoint(Pen newPen)
    {
        if (!IsPenSizeCompatible(newPen)) return false;

        Adopt(newPen);
        return true;
    }

    // Takes a size-compatible pen: dashes of other lengths are written over the old, and cut again.
    private void Adopt(Pen pen)
    {
        var patternChanged = _hasDashes && PatternChanged(pen);
        _pen = pen;
        if (!patternChanged) return;

        foreach (var c in _contours)
        {
            SetPattern(c, pen);
            c.VertexBuffer?.Invalidate();
        }
    }

    // Same-topology geometry update (a resize): if the new contours match the current ones (count + per-contour point
    // count + closedness) and the pen stays size-compatible, rewrite the points - and the dashes, for a mask - in place:
    // no new component, no allocation. Returns false (caller rebuilds) for the cut (its output size depends on arc
    // length) or any topology/pen-size change.
    public bool TryUpdateGeometry(IReadOnlyList<(Vector2<double>[] Points, bool IsClosed)> contours, Pen pen)
    {
        if (_useCut) return false;
        if (!IsPenSizeCompatible(pen)) return false;

        var valid = Valid(contours);
        if (_contours.Exists(c => c.Batched))
        {
            // The same contours, point for point and open or closed alike, fall into the same batches: their tables
            // stand, only points move. Checked whole before anything is written.
            var parts = Batches(valid);
            if (parts.Count != _contours.Count) return false;
            for (var i = 0; i < parts.Count; i++)
                if (!Matches(_contours[i], valid, parts[i].Start, parts[i].Count)) return false;

            for (var i = 0; i < parts.Count; i++)
            {
                var c = _contours[i];
                if (c.LaidOutElsewhere)
                {
                    c.PointsData = new float[c.PointsData.Length];
                    c.ContoursData = new uint[c.ContoursData.Length];
                    c.PointContoursData = new uint[c.PointContoursData.Length];
                    c.ArcsData = new float[c.ArcsData.Length];
                    c.LaidOutElsewhere = false;
                }

                if (c.Batched) FillBatch(c, valid, parts[i].Start, parts[i].Count);
                else c.PointsData = ToFloats(valid[parts[i].Start].Points);
                FillArcs(c);
                c.PointsBuffer.Invalidate();
                c.ArcsBuffer.Invalidate();
            }

            Adopt(pen);
            return true;
        }

        if (valid.Count != _contours.Count) return false;
        for (var i = 0; i < valid.Count; i++)
            if (valid[i].Points.Length != _contours[i].PointCount || valid[i].IsClosed != _contours[i].IsClosed)
                return false;

        for (var i = 0; i < valid.Count; i++)
        {
            var c = _contours[i];
            c.PointsData = ToFloats(valid[i].Points);
            FillArcs(c);
            c.PointsBuffer.Invalidate();
            c.ArcsBuffer.Invalidate();
        }

        Adopt(pen);
        return true;
    }

    private static float[] ToFloats(Vector2<double>[] points)
    {
        var floats = new float[points.Length * 2];
        for (var i = 0; i < points.Length; i++)
        {
            floats[i * 2] = (float)points[i].X;
            floats[i * 2 + 1] = (float)points[i].Y;
        }
        return floats;
    }

    // How much the dash pattern must stretch for a WHOLE number of periods to go round this closed contour. Never more
    // than half a period either way, so the dashes keep the length they were authored with to within a few percent.
    private static float FitScale(Vector2<double>[] points, Pen pen)
    {
        var period = 0.0;
        foreach (var entry in pen.Dashes) period += entry;
        if (period <= 0) return 1f;

        var length = 0.0;
        for (var i = 0; i < points.Length; i++)
        {
            var next = i + 1 == points.Length ? 0 : i + 1;   // closed: the last segment runs back to the start
            length += (points[next] - points[i]).Length();
        }

        if (length <= 0) return 1f;

        var periods = Math.Max(1.0, Math.Round(length / period));
        return (float)(length / (periods * period));
    }

    // Builds the GPU buffers for one contour. Returns null for a degenerate contour (no thread/vertex work).
    private Contour BuildContour(Vector2<double>[] points, bool isClosed)
    {
        if (points.Length < 2) return null;

        var c = new Contour { IsClosed = isClosed, PointCount = (uint)points.Length, PointsData = ToFloats(points) };

        // Disc segments are needed by the JOIN only: round (a disc) or bevel (a wedge) on the continuous path, round on
        // the cut path (a disc at every corner a dash crosses; a bevel wedge is written inline there). Caps need none -
        // they are a per-fragment mask now, which is also why a round cap no longer drags a 16-triangle fan onto every
        // dash of every dashed stroke.
        c.RoundSegments = _useCut ? CutRoundSegments : ExpandJoinTriangles;

        // Raw contour -> BDA storage buffer (the GPU reads it; dashing/trim happen on the GPU, not here).
        c.PointsBuffer = ToDispose(BufferManager.CreateBuffer(
            BufferUsageFlags.StorageBuffer | BufferUsageFlags.ShaderDeviceAddress, StrokeMemory, "stroke points"));
        c.PointsBuffer.Reserve(c.PointsBytes);
        c.PointsBuffer.Invalidate();

        if (_useCut)
        {
            c.ThreadCount = 1;   // a single thread walks the whole contour and cuts the dash/trim pieces
            c.Arcs = [ArcOf(points, isClosed)];
            c.VertexCount = CutCapacity(c.Arcs[0], _pen, c.RoundSegments, DashRoom);   // worst-case piece bound
            c.Room = [c.VertexCount];

            // A CLOSED contour's length almost never divides by the dash pattern, and the remainder lands where the
            // contour closes - one long dash at the seam, moving as the shape resizes. Fitting stretches the pattern by
            // at most half a period so a whole number goes round; the offset is scaled to match when it is uploaded.
            c.DashScale = _hasDashes && isClosed && _pen.FitDashesToContour
                ? FitScale(points, _pen)
                : 1f;
            SetPattern(c, _pen);

            c.IndirectBuffer = ToDispose(Buffer.New(_device, (ulong)(4 * sizeof(uint)),
                BufferUsageFlags.IndirectBuffer | BufferUsageFlags.StorageBuffer | BufferUsageFlags.ShaderDeviceAddress, StrokeMemory));
        }
        else
        {
            // Drawn straight from its points (StrokePullVS): as many vertices a point as the expander would write.
            c.ThreadCount = c.PointCount;
            c.VertexCount = c.PointCount * (6u + c.RoundSegments * 3u);
            c.ArcsData = new float[points.Length];
            FillArcs(c);
            c.ArcsBuffer = NewStorage((ulong)(points.Length * sizeof(float)));
            if (_hasDashes) SetPattern(c, _pen);
            return c;
        }

        if (c.ThreadCount == 0 || c.VertexCount == 0) return null;

        // StrokeVertex's layout.
        c.VertexBuffer = ToDispose(BufferManager.CreateBuffer(
            BufferUsageFlags.VertexBuffer | BufferUsageFlags.StorageBuffer | BufferUsageFlags.ShaderDeviceAddress, VertexMemory,
            "stroke vertices"));
        c.VertexBuffer.Reserve(c.VertexBytes);
        c.VertexBuffer.Invalidate();
        return c;
    }

    // The dash pattern as the shader reads it, stretched by scale: the real dash array, or a 1-element dummy for trim-only
    // (PatternCount=0 -> not read).
    private void SetPattern(Contour c, Pen pen)
    {
        var dashes = pen.Dashes;
        var count = dashes?.Count ?? 0;
        var pattern = c.PatternData is { } held && held.Length == Math.Max(1, count) ? held : new float[Math.Max(1, count)];
        for (var i = 0; i < count; i++)
            pattern[i] = _useCut ? DashLength(dashes[i] * c.DashScale) : (float)(dashes[i] * c.DashScale);
        c.PatternData = pattern;
        c.PatternBuffer ??= NewStorage((ulong)(pattern.Length * sizeof(float)));
        c.PatternBuffer.Invalidate();
    }

    // Each dash segment is at least 1px. The CUT path turns every dash into REAL geometry, and a sub-pixel piece becomes a
    // degenerate quad that never rasterizes - the line breaks up at DashOn<1. The SDF batch is analytic (per-fragment
    // coverage) so a sub-pixel dash just fades there; only this compute path needs the floor.
    private static float DashLength(double length) => Math.Max((float)length, 1.0f);

    // The pattern's length as the shader reads it at `scale`, which DashPhase counts in.
    private static double DashPeriod(Pen pen, double scale, bool floored)
    {
        var period = 0.0;
        foreach (var entry in pen.Dashes) period += floored ? DashLength(entry * scale) : entry * scale;
        return period;
    }

    private static (double Length, int Segments) ArcOf(Vector2<double>[] pts, bool closed)
    {
        int n = pts.Length;
        int segs = closed ? n : n - 1;
        double total = 0;
        for (var s = 0; s < segs; s++) total += (pts[(s + 1) % n] - pts[s]).Length();
        return (total, segs);
    }

    // Room for twice the dashes the pattern needs, so shorter ones still fit.
    private const double DashRoom = 2;

    // Worst-case vertex count for the dash output buffer: a piece per "on" run of every period the arc length holds,
    // plus a per-segment allowance for corner splits. A piece is exactly its 6 quad verts (caps are a mask, they emit
    // nothing), plus one corner join per segment (a round disc, or up to 3 bevel/miter triangles). Capped at what one
    // contour may write. This is the only CPU work dashing does - an O(points) length sum, no per-piece tessellation.
    private static uint CutCapacity((double Length, int Segments) arc, Pen pen, uint roundSegments, double room)
    {
        const long perPiece = 6;
        var segs = arc.Segments;
        long joinVerts = (long)segs * (roundSegments > 0 ? roundSegments * 3 : 9);   // one join per corner

        // Trim-only (no dashes): trim clips to at most one run, so segs + 2 boundary splits is the bound.
        if (pen.Dashes is not { Count: > 0 })
            return (uint)Math.Clamp((segs + 2) * perPiece + joinVerts, 6, MaxDashVertices);

        // The period as the cut walks it, each entry floored to 1px. Fitted to a closed contour, a whole number of periods
        // goes round - round(L/P), at most 2L/P + 1 - so twice the periods it holds and one more bounds them. An odd
        // pattern turns its runs on and off in turn, so each entry may be a run.
        double period = 0;
        foreach (var entry in pen.Dashes) period += DashLength(entry);
        long periods = (long)Math.Ceiling(arc.Length * room / (period * 0.5)) + 1;
        long pieces = periods * pen.Dashes.Count + segs + 4;
        return (uint)Math.Clamp(pieces * perPiece + joinVerts, 6, MaxDashVertices);
    }

    // Cap mapping for the expander shaders. Every cap renders as its true shape: 0 = flat, 1 = square, 2 = convex round,
    // 3 = convex triangle (tip), 4 = concave triangle (V notch), 5 = concave round (carved arc). The shader stretches
    // the end quad far enough for the shape (CapExtend) and CARVES it per fragment (CapSd) - no cap geometry exists.
    private static uint MapCap(PenLineCap cap) => cap switch
    {
        PenLineCap.Square => 1u,
        PenLineCap.ConvexRound => 2u,
        PenLineCap.ConvexTriangle => 3u,
        PenLineCap.ConcaveTriangle => 4u,
        PenLineCap.ConcaveRound => 5u,
        _ => 0u   // Flat
    };

    // Join mapping: 0 = miter (bisector ribbon, clamped), 1 = bevel (per-segment rectangles + corner wedge), 2 = round (rectangles + disc fan).
    private static uint MapJoin(PenLineJoin join) => join switch
    {
        PenLineJoin.Round => 2u,
        PenLineJoin.Bevel => 1u,
        _ => 0u
    };

    // How far the ribbon is widened for the AA feather, in geometry-LOCAL units: about one device pixel. The pixel shader
    // feathers by the true width of a pixel where it draws (screen derivatives); this only leaves it room. Under a flat
    // transform a pixel is as wide everywhere: one over the area scale. Under a perspective one it is widest where the
    // stroke lies farthest, so the room is taken there, from the projection's own derivatives at the corners of what the
    // stroke covers - the top-left 2x2 of a projective matrix is no scale at all, and passing through zero at some lean
    // it widened the feather across the whole stroke.
    private float ComputeFringe()
    {
        if (!AnalyticAa.Enabled) return 0f;   // AA off: no feather - the ribbon stays a hard +/-HalfThickness band
        var deviceScale = DeviceScale();
        return deviceScale > 1e-4f ? 1.0f / deviceScale : 1.0f;
    }

    // Device pixels per geometry-local unit, the least over the stroke under a perspective.
    private float DeviceScale()
    {
        var t = RenderData.TransformMatrix;
        var worldScale = t.M14 == 0f && t.M24 == 0f
            ? MathF.Sqrt(MathF.Abs(t.M11 * t.M22 - t.M12 * t.M21))
            : LeastProjectedScale(t);
        return worldScale * (float)RenderData.RenderScale;
    }

    // Half the line's width in geometry-local units: the pen's thickness in its unit, no narrower than its minimum.
    private float HalfThickness()
    {
        var thickness = _pen.Thickness;
        if (_pen.ThicknessUnit == PenThicknessUnit.DevicePixels || _pen.MinimumDeviceThickness > 0)
        {
            var deviceScale = DeviceScale();
            if (deviceScale > 1e-4f)
            {
                if (_pen.ThicknessUnit == PenThicknessUnit.DevicePixels) thickness /= deviceScale;
                thickness = Math.Max(thickness, _pen.MinimumDeviceThickness / deviceScale);
            }
        }

        return (float)(thickness / 2.0);
    }

    // The least local->window scale over the corners of the contours' extent, w-divided as the rasterizer divides.
    private float LeastProjectedScale(Matrix4x4F t)
    {
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var c in _contours)
        {
            for (var i = 0; i + 1 < c.PointsData.Length; i += 2)
            {
                minX = MathF.Min(minX, c.PointsData[i]);
                maxX = MathF.Max(maxX, c.PointsData[i]);
                minY = MathF.Min(minY, c.PointsData[i + 1]);
                maxY = MathF.Max(maxY, c.PointsData[i + 1]);
            }
        }

        if (minX > maxX) return ProjectedScaleAt(t, 0f, 0f);

        var least = float.MaxValue;
        foreach (var (x, y) in (ReadOnlySpan<(float, float)>)[(minX, minY), (maxX, minY), (minX, maxY), (maxX, maxY)])
        {
            var scale = ProjectedScaleAt(t, x, y);
            if (scale > 0f) least = MathF.Min(least, scale);
        }

        return least == float.MaxValue ? 0f : least;
    }

    // The area scale of the projective map at (x, y): the determinant of its derivatives after the divide by w; 0 behind
    // the eye.
    internal static float ProjectedScaleAt(Matrix4x4F t, float x, float y)
    {
        var w = x * t.M14 + y * t.M24 + t.M44;
        if (!(w > 1e-6f)) return 0f;
        var sx = (x * t.M11 + y * t.M21 + t.M41) / w;
        var sy = (x * t.M12 + y * t.M22 + t.M42) / w;
        var dxdx = (t.M11 - sx * t.M14) / w;
        var dxdy = (t.M21 - sx * t.M24) / w;
        var dydx = (t.M12 - sy * t.M14) / w;
        var dydy = (t.M22 - sy * t.M24) / w;
        return MathF.Sqrt(MathF.Abs(dxdx * dydy - dxdy * dydx));
    }

    // Whether the cut drawn scaled to this width keeps a flat end's feather within a quarter of a pixel.
    private bool DrawsAt(float half, float fringe)
    {
        if (float.IsNaN(_expandedHalf)) return false;
        var written = _expandedHalf + _expandedFringe * 0.5f;
        if (written <= 0f) return half == _expandedHalf && fringe == _expandedFringe;
        var scale = (half + fringe * 0.5f) / written;
        return Math.Abs(fringe - scale * _expandedFringe) * 0.5f <= 0.25f * fringe;
    }

    // Compute runs OUT of the render pass: PreRender is recorded from BeginDraw's beforeRenderPass hook
    // (WindowRenderService.BeginDraw -> ForwardWindowRenderer.PreRender -> RenderUnit.PreRender -> here). Each contour
    // uploads its points to this frame's slot; a cut one is cut again into this frame's vertex slot when stale.
    public override void PreRender()
    {
        var fringe = ComputeFringe();
        var half = HalfThickness();
        var stale = !DrawsAt(half, fringe);
        if (stale)
        {
            _expandedHalf = half;
            _expandedFringe = fringe;
        }

        // What the CUT is made of. Re-cutting unconditionally cost a dashed stroke a compute dispatch every frame even
        // when it stood still - the difference between 600 and 200 fps on a page of dashed shapes. The width is not in
        // it: the shader draws the ribbon at any width.
        var cut = (_pen.DashOffset, _pen.DashPhase, _pen.TrimStart, _pen.TrimEnd, _pen.StartLineCap, _pen.EndLineCap,
            _pen.DashStartCap, _pen.DashEndCap);
        var cutChanged = _cutDirty || cut != _expandedCut;
        _expandedCut = cut;
        _cutDirty = false;

        foreach (var c in _contours)
        {
            var points = c.PointsBuffer.Acquire(c.PointsBytes, out var writePoints);
            if (writePoints) points.SetData(c.PointsData, 0, (uint)c.PointsData.Length);
            Buffer contoursTable = null;
            Buffer pointContours = null;
            Buffer slots = null;
            if (c.Batched)
            {
                contoursTable = c.ContoursBuffer.Acquire((ulong)(c.ContoursData.Length * sizeof(uint)), out var writeTable);
                if (writeTable) contoursTable.SetData(c.ContoursData, 0, (uint)c.ContoursData.Length);
                pointContours = c.PointContoursBuffer.Acquire((ulong)(c.PointContoursData.Length * sizeof(uint)),
                    out var writeOwners);
                if (writeOwners) pointContours.SetData(c.PointContoursData, 0, (uint)c.PointContoursData.Length);
                if (c.SlotsData != null)
                {
                    slots = c.SlotsBuffer.Acquire((ulong)(c.SlotsData.Length * sizeof(uint)), out var writeSlots);
                    if (writeSlots) slots.SetData(c.SlotsData, 0, (uint)c.SlotsData.Length);
                }
            }

            if (!_useCut)
            {
                var arcs = c.ArcsBuffer.Acquire((ulong)(c.ArcsData.Length * sizeof(float)), out var writeArcs);
                if (writeArcs) arcs.SetData(c.ArcsData, 0, (uint)c.ArcsData.Length);
                if (c.PatternBuffer != null)
                {
                    var dashes = c.PatternBuffer.Acquire((ulong)(c.PatternData.Length * sizeof(float)), out var writeDashes);
                    if (writeDashes) dashes.SetData(c.PatternData, 0, (uint)c.PatternData.Length);
                }

                continue;
            }

            if (stale || cutChanged) c.VertexBuffer.Invalidate();

            var vertices = c.VertexBuffer.Acquire(c.VertexBytes, out var writeVertices);
            if (!writeVertices) continue;   // this slot already holds the cut

            _effect.Points.SetValue(points.GetDeviceAddress());
            _effect.Output.SetValue(vertices.GetDeviceAddress());
            _effect.PointCount.SetValue(c.PointCount);
            // The effect is shared by every stroke: a batch says so, and every other contour says it is none.
            _effect.Batched.SetValue(c.Batched ? 1u : 0u);
            if (c.Batched)
            {
                _effect.Contours.SetValue(contoursTable.GetDeviceAddress());
                _effect.PointContours.SetValue(pointContours.GetDeviceAddress());
            }
            _effect.IsClosed.SetValue(c.IsClosed ? 1u : 0u);
            _effect.JoinType.SetValue(_joinType);
            _effect.RoundSegments.SetValue(c.RoundSegments);
            // Start/EndLineCap belong to the stroke's REAL ends, and a closed contour only has them where a trim window
            // cuts one (that is what puts a round end on a progress ring). Untrimmed and closed, there is no real end
            // at all, so the seam wears the dash caps like every other dash boundary instead of a stray line cap.
            var hasRealEnds = !c.IsClosed || _pen.TrimStart > 0.0 || _pen.TrimEnd < 1.0;
            _effect.StartCap.SetValue(MapCap(hasRealEnds ? _pen.StartLineCap : _pen.DashStartCap));
            _effect.EndCap.SetValue(MapCap(hasRealEnds ? _pen.EndLineCap : _pen.DashEndCap));
            // The two ends of a dash are separate properties: one dash can be a concave bite and a convex tip - an arrow.
            _effect.DashStartCap.SetValue(MapCap(_pen.DashStartCap));
            _effect.DashEndCap.SetValue(MapCap(_pen.DashEndCap));
            _effect.HalfThickness.SetValue(half);
            _effect.Fringe.SetValue(fringe);   // AA feather: the expander widens the ribbon/discs by Fringe/2

            if (!c.Batched) _effect.Indirect.SetValue(c.IndirectBuffer.GetDeviceAddress());
            var pattern = c.PatternBuffer.Acquire((ulong)(c.PatternData.Length * sizeof(float)), out var writePattern);
            if (writePattern) pattern.SetData(c.PatternData, 0, (uint)c.PatternData.Length);
            _effect.Pattern.SetValue(pattern.GetDeviceAddress());
            _effect.PatternCount.SetValue(_hasDashes ? (uint)_pen.Dashes.Count : 0u);
            _effect.MaxVertices.SetValue(c.VertexCount);
            // The phase is in PERIODS, so it becomes pixels here - which is exactly the arithmetic the author would
            // otherwise be doing by hand. Both parts then scale with the contour fit, or the ring, seamless in
            // shape, would still drift in phase as it marches.
            var period = _hasDashes ? DashPeriod(_pen, c.DashScale, floored: true) : 0.0;
            var effectiveOffset = (float)(_pen.DashOffset * c.DashScale + _pen.DashPhase * period);
            if (Environment.GetEnvironmentVariable("ADAM_DASH_LOG") == "1")
            {
                Console.Error.WriteLine(
                    $"[DASH] phase={_pen.DashPhase:F3} pxOffset={_pen.DashOffset:F2} period={period:F1} " +
                    $"scale={c.DashScale:F3} -> {effectiveOffset:F2}");
            }
            _effect.DashOffset.SetValue(effectiveOffset);
            _effect.TrimStart.SetValue((float)Math.Clamp(_pen.TrimStart, 0.0, 1.0));
            _effect.TrimEnd.SetValue((float)Math.Clamp(_pen.TrimEnd, 0.0, 1.0));
            if (c.Batched)
            {
                _effect.ContourCount.SetValue(c.ThreadCount);
                _effect.Slots.SetValue(slots.GetDeviceAddress());
                _effect.StrokeDashCutBatchPass.Apply();   // a thread per contour, each into its own output range
            }
            else
            {
                _effect.StrokeDashCutPass.Apply();   // separate compute kernel for the dash/trim cut
            }

            _device.Dispatch((c.ThreadCount + 63) / 64);

            // Make the cut visible to the draw: the vertex read and, for a lone contour, the indirect-command read.
            _device.BufferBarrier(vertices,
                PipelineStageFlagBits2.ComputeShaderBit, AccessFlagBits2.ShaderWriteBit,
                PipelineStageFlagBits2.VertexAttributeInputBit, AccessFlagBits2.VertexAttributeReadBit);
            if (!c.Batched)
            {
                _device.BufferBarrier(c.IndirectBuffer,
                    PipelineStageFlagBits2.ComputeShaderBit, AccessFlagBits2.ShaderWriteBit,
                    PipelineStageFlagBits2.DrawIndirectBit, AccessFlagBits2.IndirectCommandReadBit);
            }
        }
    }

    public override void Render()
    {
        if (_contours.Count == 0) return;

        // Points are in geometry-local space: WVP = transform(local->world) * projection(world->clip). The pen color
        // and transform are shared by every contour, so set them once, then draw each contour's expanded ribbon.
        _effect.Projection.SetValue(RenderData.TransformMatrix * RenderData.ProjectionMatrix);
        var color = (_pen.Brush as SolidColorBrush)?.Color.ToVector4() ?? new Vector4F(0, 0, 0, 1);
        color.W *= (float)(_pen.Brush?.Opacity ?? 1.0) * RenderData.Opacity;   // color alpha x pen-brush Opacity x element Opacity
        _effect.StrokeColor.SetValue(color);
        // StrokePS reads these for the analytic-AA coverage (distance vs HalfThickness +/- Fringe/2).
        _effect.HalfThickness.SetValue(HalfThickness());
        _effect.Fringe.SetValue(ComputeFringe());

        _device.VertexType = _useCut ? typeof(StrokeVertex) : null;   // a continuous stroke is read by address
        _device.PolygonMode = PolygonMode.Fill;
        _device.PrimitiveTopology = PrimitiveTopology.TriangleList;
        _device.ColorBlendEquation = ColorBlendEquations.AlphaBlend;
        _device.DepthTestEnabled = true;

        // An OPAQUE stroke cannot see its own overlaps, and a fully transparent one has nothing to show; both keep the
        // single cheap pass. A translucent one blends every self-crossing twice and comes out veined, so it goes
        // through the union path below instead.
        if (color.W >= 1.0f || color.W <= 0.0f || !_device.HasDepthAttachment || !TryDeviceBounds(out var bounds))
        {
            _device.DepthCompareFunction = CompareOp.Always;
            _device.DepthWriteEnable = true;
            DrawContours(_effect.StrokeDrawPass, _effect.StrokePullDrawPass);
            return;
        }

        // Depth is SCRATCH here, and it is shared with everything else in the frame, so this stroke starts by putting
        // its own bounds back to 1.0 rather than assuming what is there. Depth only - the stencil in that rectangle
        // carries the instanced fills' coverage marks and must survive.
        _device.ClearDepthInRect(bounds);

        // Pass 1: settle the union's coverage into that depth, no color. Pass 2: the one fragment that survived blends.
        // Both passes must be draws of the SAME ribbon so their depths agree bit for bit.
        _device.ColorComponentFlags = 0;
        _device.DepthCompareFunction = CompareOp.Less;
        _device.DepthWriteEnable = true;
        DrawContours(_effect.StrokeUnionDepthPass, _effect.StrokePullUnionDepthPass);

        _device.ColorComponentFlags = AllColorComponents;
        _device.DepthCompareFunction = CompareOp.Equal;
        _device.DepthWriteEnable = false;
        DrawContours(_effect.StrokeUnionDrawPass, _effect.StrokePullUnionDrawPass);

        _device.DepthCompareFunction = CompareOp.Always;
        _device.DepthWriteEnable = true;
    }

    // This stroke's footprint in DEVICE pixels - the rectangle whose depth it may use as scratch. Built from the raw
    // contour points padded by the widest thing the expander can put around them (half a thickness, the miter clamp's
    // 4x reach on a sharp corner, the AA feather), then transformed and clamped to the viewport.
    private bool TryDeviceBounds(out Rect2D bounds)
    {
        bounds = default;
        var viewports = _device.CurrentViewports;
        if (viewports is not { Length: > 0 }) return false;

        // The miter clamp lets a corner reach 4x the WIDENED half-extent (half + Fringe/2): 2 thicknesses + 2 fringes.
        var pad = 4.0f * HalfThickness() + 2.0f * ComputeFringe() + 1.0f;
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var c in _contours)
        {
            for (var i = 0; i + 1 < c.PointsData.Length; i += 2)
            {
                if (c.PointsData[i] < minX) minX = c.PointsData[i];
                if (c.PointsData[i] > maxX) maxX = c.PointsData[i];
                if (c.PointsData[i + 1] < minY) minY = c.PointsData[i + 1];
                if (c.PointsData[i + 1] > maxY) maxY = c.PointsData[i + 1];
            }
        }
        if (minX > maxX) return false;

        minX -= pad; minY -= pad; maxX += pad; maxY += pad;

        // Local -> clip, row-vector convention (the same product StrokeVS uses). A rotation or a flip can send any
        // corner anywhere, so all four are transformed and the extent taken from the result.
        var m = RenderData.TransformMatrix * RenderData.ProjectionMatrix;
        float clipMinX = float.MaxValue, clipMinY = float.MaxValue, clipMaxX = float.MinValue, clipMaxY = float.MinValue;
        foreach (var (x, y) in new[] { (minX, minY), (maxX, minY), (minX, maxY), (maxX, maxY) })
        {
            // A perspective puts depth in w; a corner behind the eye leaves no bounds to trust.
            var w = x * m.M14 + y * m.M24 + m.M44;
            if (!(w > 1e-6f)) return false;
            var cx = (x * m.M11 + y * m.M21 + m.M41) / w;
            var cy = (x * m.M12 + y * m.M22 + m.M42) / w;
            if (cx < clipMinX) clipMinX = cx;
            if (cx > clipMaxX) clipMaxX = cx;
            if (cy < clipMinY) clipMinY = cy;
            if (cy > clipMaxY) clipMaxY = cy;
        }

        var vp = viewports[0];
        var left = (int)Math.Floor((clipMinX * 0.5f + 0.5f) * vp.Width + vp.X);
        var top = (int)Math.Floor((clipMinY * 0.5f + 0.5f) * vp.Height + vp.Y);
        var right = (int)Math.Ceiling((clipMaxX * 0.5f + 0.5f) * vp.Width + vp.X);
        var bottom = (int)Math.Ceiling((clipMaxY * 0.5f + 0.5f) * vp.Height + vp.Y);

        left = Math.Clamp(left, (int)vp.X, (int)(vp.X + vp.Width));
        top = Math.Clamp(top, (int)vp.Y, (int)(vp.Y + vp.Height));
        right = Math.Clamp(right, left, (int)(vp.X + vp.Width));
        bottom = Math.Clamp(bottom, top, (int)(vp.Y + vp.Height));
        if (right == left || bottom == top) return false;

        bounds = new Rect2D
        {
            Offset = new Offset2D { X = left, Y = top },
            Extent = new Extent2D { Width = (uint)(right - left), Height = (uint)(bottom - top) }
        };
        return true;
    }

    // A pulled contour's buffers are read by address, set before its pass is applied.
    private void DrawContours(IEffectPass cut, IEffectPass pulled)
    {
        if (_useCut)
        {
            _effect.DashMask.SetValue(0u);
            cut.Apply();
            foreach (var c in _contours)
            {
                var vertices = c.VertexBuffer.Acquire(c.VertexBytes, out _);   // the slot PreRender cut this frame
                _device.SetVertexBuffer(vertices);
                if (!c.Batched)
                    _device.DrawIndirect(vertices, c.IndirectBuffer);   // GPU-decided vertex count (dash/trim)
                else
                    _device.Draw(c.VertexCount, 1);   // a cut batch draws every range whole: what is unused draws nothing
            }

            return;
        }

        _effect.JoinType.SetValue(_joinType);
        _effect.StartCap.SetValue(MapCap(_pen.StartLineCap));
        _effect.EndCap.SetValue(MapCap(_pen.EndLineCap));
        _effect.DashMask.SetValue(_hasDashes ? 1u : 0u);
        if (_hasDashes)
        {
            _effect.PatternCount.SetValue((uint)_pen.Dashes.Count);
            _effect.DashOffset.SetValue((float)(_pen.DashOffset + _pen.DashPhase * DashPeriod(_pen, 1, floored: false)));
        }

        foreach (var c in _contours)
        {
            if (c.PatternBuffer != null)
            {
                _effect.Pattern.SetValue(c.PatternBuffer.Acquire((ulong)(c.PatternData.Length * sizeof(float)), out _)
                    .GetDeviceAddress());
            }

            _effect.Points.SetValue(c.PointsBuffer.Acquire(c.PointsBytes, out _).GetDeviceAddress());
            _effect.Arcs.SetValue(c.ArcsBuffer.Acquire((ulong)(c.ArcsData.Length * sizeof(float)), out _)
                .GetDeviceAddress());
            _effect.PointCount.SetValue(c.PointCount);
            _effect.IsClosed.SetValue(c.IsClosed ? 1u : 0u);
            _effect.RoundSegments.SetValue(c.RoundSegments);
            _effect.Batched.SetValue(c.Batched ? 1u : 0u);
            if (c.Batched)
            {
                _effect.Contours.SetValue(c.ContoursBuffer
                    .Acquire((ulong)(c.ContoursData.Length * sizeof(uint)), out _).GetDeviceAddress());
                _effect.PointContours.SetValue(c.PointContoursBuffer
                    .Acquire((ulong)(c.PointContoursData.Length * sizeof(uint)), out _).GetDeviceAddress());
            }

            pulled.Apply();
            _device.Draw(c.VertexCount, 1);
        }
    }
}
