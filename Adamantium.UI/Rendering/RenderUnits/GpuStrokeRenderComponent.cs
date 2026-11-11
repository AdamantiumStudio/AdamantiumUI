using System;
using System.Collections.Generic;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.EffectsFramework;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Imaging;
using Adamantium.UI.FX;
using Adamantium.Vulkan.Core;

namespace Adamantium.UI.Rendering.RenderUnits;

/// <summary>
/// GPU strokes. A stroke keeps only its points: the vertex shader works out its ribbon where it is drawn, at any width,
/// and the pixel shader shapes its caps, trim window and dash runs along it - nothing is cut. Buffers are rented, so
/// same-topology changes rewrite in place.
/// </summary>
public sealed class GpuStrokeRenderComponent : UIRenderComponent
{
    // Disc-fan subdivision for round joins (a smooth-enough circle); 0 when the contour has no round geometry.
    private const uint RoundSegmentsCount = 16;

    private const float RefitTolerance = 1e-4f;

    private const ColorComponentFlagBits AllColorComponents = ColorComponentFlagBits.RBit |
                                                              ColorComponentFlagBits.GBit |
                                                              ColorComponentFlagBits.BBit |
                                                              ColorComponentFlagBits.ABit;


    private const MemoryPropertyFlags StrokeMemory = MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.DeviceLocal;

    private readonly IGraphicsDevice _device;
    private readonly StrokeEffect _effect;
    private readonly GradientUniforms _gradient;
    private Vector4F _localBounds;          // the shape's bounds, which a gradient brush spans
    private Pen _pen;                       // mutable: TryRepoint swaps it for a uniform-only pen change (animation)
    private readonly bool _hasDashes;
    private float _fittedAtScale = float.NaN;
    private readonly uint _joinType;        // 0 miter, 1 bevel, 2 round
    private readonly List<Contour> _contours = [];

    // Per-contour GPU state: a contour standing alone, or a batch of them drawn by one draw.
    private sealed class Contour
    {
        public bool IsClosed;
        public uint RoundSegments;   // the triangles a point keeps for its join - depends on the join only (caps are analytic)
        public uint PointCount;
        public uint VertexCount;     // the draw's vertex count
        public float[] PointsData;   // x,y interleaved - re-uploaded to the current ring slot when stale
        public ReusableBuffer PointsBuffer;
        public float[] ArcsData;              // each point's distance along its contour
        public ReusableBuffer ArcsBuffer;
        public float[] PatternData;           // dashes as read, stretched by DashScale
        public ReusableBuffer PatternBuffer;
        public float DashScale = 1f;     // what FitDashesToContour stretched the pattern by (1 = untouched)

        // A BATCH: many contours, their points one after another, drawn by one draw. ContoursData is (first point, point
        // count, closed) per contour; PointContoursData the contour of each point.
        public bool Batched;
        public bool LaidOutElsewhere;   // its arrays are a PreparedStrokeBatch's: shared, so copied before written
        public uint[] ContoursData;
        public uint[] PointContoursData;
        public ReusableBuffer ContoursBuffer;
        public ReusableBuffer PointContoursBuffer;

        public ulong PointsBytes => (ulong)(PointsData.Length * sizeof(float));
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
        _joinType = MapJoin(pen.PenLineJoin);

        // Strokes of many contours - a map's roads, a chart's series, a map's dashed paths - go as ONE batch: a draw per
        // contour is what thousands of them cannot afford. Dashed or trimmed, each contour of a batch still lays its runs
        // by its own arc length; one whose dashes are fitted to it keeps a draw of its own, as its pattern is its own.
        var valid = Valid(contours);
        _gradient = new GradientUniforms(effect);
        _localBounds = GradientUniforms.BoundsOf(valid);
        IReadOnlyList<(Vector2<double>[] Points, bool IsClosed)> alone = [];
        var together = valid;
        if (_hasDashes && _pen.FitDashesToContour)
        {
            var fitted = new List<(Vector2<double>[] Points, bool IsClosed)>();
            var open = new List<(Vector2<double>[] Points, bool IsClosed)>();
            foreach (var contour in valid) (contour.IsClosed ? fitted : open).Add(contour);
            alone = fitted;
            together = fitted.Count > 0 ? open : valid;
        }

        if (together.Count > 1)
        {
            var runs = Batches(together);
            // The laid-out batches are of ALL the contours; with fitted ones set apart these are another list.
            var reuse = alone.Count == 0 && laidOut != null && laidOut.Length == runs.Count;
            for (var r = 0; r < runs.Count; r++)
            {
                var (start, count) = runs[r];
                var contour = count == 1 ? BuildContour(together[start].Points, together[start].IsClosed)
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

    /// <summary>What draw <paramref name="draw"/>'s dash pattern is stretched by to fit its contour (1 = not at all).</summary>
    internal float DashScaleOf(int draw) => _contours[draw].DashScale;

    private static IReadOnlyList<(Vector2<double>[] Points, bool IsClosed)> Valid(
        IReadOnlyList<(Vector2<double>[] Points, bool IsClosed)> contours) => PreparedStrokeBatch.Valid(contours);

    private static List<(int Start, int Count)> Batches(IReadOnlyList<(Vector2<double>[] Points, bool IsClosed)> contours) =>
        PreparedStrokeBatch.Batches(contours);

    // The triangles a point of the contour draws for its join, and so its draw's vertices: a round join's fan, a bevel's
    // two - and a miter's two only where some corner reaches past the limit, where it is beveled.
    private void SetJoinTriangles(Contour c)
    {
        c.RoundSegments = _joinType switch
        {
            2u => RoundSegmentsCount,
            1u => 2u,
            _ => AnyCornerPastLimit(c) ? 2u : 0u,
        };
        c.VertexCount = c.PointCount * (6u + c.RoundSegments * 3u);
    }

    private bool AnyCornerPastLimit(Contour c)
    {
        var limit = MiterLimitValue;
        if (!c.Batched) return AnyCornerPastLimit(c.PointsData, 0, (int)c.PointCount, c.IsClosed, limit);
        for (var k = 0; k + 2 < c.ContoursData.Length; k += 3)
        {
            if (AnyCornerPastLimit(c.PointsData, (int)c.ContoursData[k], (int)c.ContoursData[k + 1],
                    c.ContoursData[k + 2] != 0, limit))
            {
                return true;
            }
        }

        return false;
    }

    // The shader's test (StrokeEffect.fx PastMiterLimit): the cosine of half the turn times the limit below 1 - with a
    // little room, so a corner the shader's float arithmetic bevels always has its two triangles.
    private static bool AnyCornerPastLimit(float[] points, int first, int count, bool closed, float limit)
    {
        for (var i = 0; i < count; i++)
        {
            if (!closed && (i == 0 || i == count - 1)) continue;
            var (prev, here, next) = (first + (i + count - 1) % count, first + i, first + (i + 1) % count);
            var (ax, ay) = (points[2 * here] - points[2 * prev], points[2 * here + 1] - points[2 * prev + 1]);
            var (bx, by) = (points[2 * next] - points[2 * here], points[2 * next + 1] - points[2 * here + 1]);
            var lengths = MathF.Sqrt(ax * ax + ay * ay) * MathF.Sqrt(bx * bx + by * by);
            if (lengths <= 0) return true;
            var halfTurnCos = MathF.Sqrt(MathF.Max(0f, (1f + (ax * bx + ay * by) / lengths) / 2f));
            if (halfTurnCos * limit < 1.001f) return true;
        }

        return false;
    }

    private float MiterLimitValue => (float)Pen.ClampMiterLimit(_pen.MiterLimit);

    // One contour standing for all of them: their points one after another, a table of where each starts, and for every
    // point the contour it is in.
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

        SetJoinTriangles(c);
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

        SetJoinTriangles(c);
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

    // True when newPen keeps every SIZE-affecting parameter (join, dash count, fitting -> same buffer sizes, fan counts
    // and batches). The rest (thickness, dash lengths, offset, trim, color, every cap - they are analytic, so they emit
    // no geometry at all) are uniforms or the pattern buffer, so a compatible pen is swapped without reallocating.
    private bool IsPenSizeCompatible(Pen newPen)
    {
        if (!Paints(newPen)) return false;   // null pen (no stroke, e.g. thickness 0) or a brush this cannot paint
        if ((newPen.Dashes is { Count: > 0 }) != _hasDashes) return false;   // a dash mask has a pattern to read
        if (MapJoin(newPen.PenLineJoin) != _joinType) return false;  // join changes the corner fan size
        if (newPen.FitDashesToContour != _pen.FitDashesToContour) return false;   // fitting decides what is batched

        // Dash pattern (buffer size + content): each GetPen() makes a NEW collection, so compare by value, not ref.
        var oldDash = _pen.Dashes; var newDash = newPen.Dashes;
        var oldN = oldDash?.Count ?? 0; var newN = newDash?.Count ?? 0;
        return oldN == newN;   // another pattern of as many entries is written over the old
    }

    /// <summary>Whether this draws <paramref name="pen"/>'s brush: any brush but the CurrentColor marker, which paints
    /// only inside a drawing, as the color of whoever shows it.</summary>
    internal static bool Paints(Pen pen) => pen?.Brush is { } brush and not CurrentColorBrush;

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

    // Takes a size-compatible pen: dashes of other lengths are written over the old.
    private void Adopt(Pen pen)
    {
        var patternChanged = _hasDashes && PatternChanged(pen);
        var limitChanged = _joinType == 0u && !Pen.ClampMiterLimit(pen.MiterLimit).Equals(Pen.ClampMiterLimit(_pen.MiterLimit));
        _pen = pen;
        if (limitChanged)
        {
            foreach (var c in _contours) SetJoinTriangles(c);
        }

        if (!patternChanged) return;

        foreach (var c in _contours)
        {
            Refit(c);
            SetPattern(c, pen);
        }
    }

    // Same-topology geometry update (a resize): if the new contours match the current ones (count + per-contour point
    // count + closedness) and the pen stays size-compatible, rewrite the points in place: no new component, no
    // allocation. Returns false (caller rebuilds) on any topology/pen-size change.
    public bool TryUpdateGeometry(IReadOnlyList<(Vector2<double>[] Points, bool IsClosed)> contours, Pen pen)
    {
        if (!IsPenSizeCompatible(pen)) return false;

        var valid = Valid(contours);
        var bounds = GradientUniforms.BoundsOf(valid);
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
                SetJoinTriangles(c);
                FillArcs(c);
                c.PointsBuffer.Invalidate();
                c.ArcsBuffer.Invalidate();
                if (c.Batched || !c.IsClosed || !_pen.FitDashesToContour) continue;

                Refit(c);
                SetPattern(c, _pen);
            }

            _localBounds = bounds;
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
            SetJoinTriangles(c);
            FillArcs(c);
            c.PointsBuffer.Invalidate();
            c.ArcsBuffer.Invalidate();
            if (!c.IsClosed || !_pen.FitDashesToContour) continue;

            // Fitted to its length, which the new points changed.
            Refit(c);
            SetPattern(c, _pen);
        }

        _localBounds = bounds;
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
    private static float FitScale(double length, Pen pen)
    {
        var period = 0.0;
        foreach (var entry in pen.Dashes) period += entry;
        if (period <= 0 || length <= 0) return 1f;

        var periods = Math.Max(1.0, Math.Round(length / period));
        return (float)(length / (periods * period));
    }

    // A lone contour's pattern stretch for the pen it is drawn with: fitted only when it is closed and the pen says so.
    // Worked out again whenever its points or its dashes change, or the seam's long dash comes back.
    private void Refit(Contour c)
    {
        if (c.Batched || !c.IsClosed || !_hasDashes || !_pen.FitDashesToContour)
        {
            c.DashScale = 1f;
            return;
        }

        // Its length: the last point's distance along it, and the closing segment back to the first - in the pen's unit,
        // which its pattern is in.
        var last = (int)c.PointCount - 1;
        var dx = (double)c.PointsData[0] - c.PointsData[last * 2];
        var dy = (double)c.PointsData[1] - c.PointsData[last * 2 + 1];
        var length = c.ArcsData[last] + Math.Sqrt(dx * dx + dy * dy);
        c.DashScale = FitScale(length / DashUnitScale(), _pen);
    }

    private void RefitToWindow()
    {
        if (!_hasDashes || !_pen.FitDashesToContour || _pen.ThicknessUnit != PenThicknessUnit.Window ||
            RenderData == null)
        {
            return;
        }

        var scale = WindowWidthScale();
        if (Math.Abs(scale - _fittedAtScale) <= RefitTolerance * scale) return;

        _fittedAtScale = scale;
        foreach (var c in _contours)
        {
            if (c.Batched || !c.IsClosed) continue;

            Refit(c);
            SetPattern(c, _pen);
        }
    }

    // Builds the GPU buffers for one contour. Returns null for a degenerate contour.
    private Contour BuildContour(Vector2<double>[] points, bool isClosed)
    {
        if (points.Length < 2) return null;

        var c = new Contour { IsClosed = isClosed, PointCount = (uint)points.Length, PointsData = ToFloats(points) };

        // Disc segments are needed by the JOIN only: round (a disc) or bevel (a wedge). Caps need none - they are a
        // per-fragment mask. Drawn straight from its points (StrokePullVS).
        SetJoinTriangles(c);
        c.PointsBuffer = NewStorage(c.PointsBytes);
        c.ArcsData = new float[points.Length];
        FillArcs(c);
        c.ArcsBuffer = NewStorage((ulong)(points.Length * sizeof(float)));

        // A CLOSED contour's length almost never divides by the dash pattern, and the remainder lands where the contour
        // closes - one long dash at the seam, moving as the shape resizes. Fitting stretches the pattern by at most half
        // a period so a whole number goes round; the offset is scaled to match when it is drawn.
        Refit(c);
        if (_hasDashes) SetPattern(c, _pen);
        return c;
    }

    // The dash pattern as the shader reads it, stretched by the contour's fit.
    private void SetPattern(Contour c, Pen pen)
    {
        var dashes = pen.Dashes;
        var count = dashes?.Count ?? 0;
        var pattern = c.PatternData is { } held && held.Length == Math.Max(1, count) ? held : new float[Math.Max(1, count)];
        for (var i = 0; i < count; i++) pattern[i] = (float)(dashes[i] * c.DashScale);
        c.PatternData = pattern;
        c.PatternBuffer ??= NewStorage((ulong)(pattern.Length * sizeof(float)));
        c.PatternBuffer.Invalidate();
    }

    // The pattern's length at `scale`, which DashPhase counts in.
    private static double DashPeriod(Pen pen, double scale)
    {
        var period = 0.0;
        foreach (var entry in pen.Dashes) period += entry * scale;
        return period;
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
    private float DeviceScale() => WindowScale() * (float)RenderData.RenderScale;

    private float WindowWidthScale()
    {
        var t = RenderData.TransformMatrix;
        if (t.M14 == 0f && t.M24 == 0f) return WindowScale();

        var plane = PlaneScale(t);
        return plane > 1e-4f ? plane : WindowScale();
    }

    private float DashUnitScale()
    {
        if (_pen.ThicknessUnit != PenThicknessUnit.Window || RenderData == null) return 1f;

        var scale = WindowWidthScale();
        return scale > 1e-4f ? 1f / scale : 1f;
    }

    // Window (layout) units per geometry-local unit.
    private float WindowScale()
    {
        var t = RenderData.TransformMatrix;
        return t.M14 == 0f && t.M24 == 0f
            ? MathF.Sqrt(MathF.Abs(t.M11 * t.M22 - t.M12 * t.M21))
            : LeastProjectedScale(t);
    }

    // Half the line's width in geometry-local units: the pen's thickness in its unit, no narrower than its minimum.
    private float HalfThickness()
    {
        var thickness = _pen.Thickness;
        if (_pen.ThicknessUnit == PenThicknessUnit.Window)
        {
            var windowScale = WindowWidthScale();
            if (windowScale > 1e-4f) thickness /= windowScale;
        }

        if (_pen.MinimumDeviceThickness > 0)
        {
            var deviceScale = DeviceScale();
            if (deviceScale > 1e-4f) thickness = Math.Max(thickness, _pen.MinimumDeviceThickness / deviceScale);
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

    /// <summary>The linear scale of a plane under the projective map <paramref name="t"/> where its w is 1 - the depth of
    /// the screen: the square root of the determinant of its x, y, w part, the area scale at any point being that over
    /// w cubed. A move of the plane within itself leaves it as it is.</summary>
    internal static float PlaneScale(Matrix4x4F t)
    {
        var determinant = t.M11 * (t.M22 * t.M44 - t.M42 * t.M24)
                          - t.M21 * (t.M12 * t.M44 - t.M42 * t.M14)
                          + t.M41 * (t.M12 * t.M24 - t.M22 * t.M14);
        return MathF.Sqrt(MathF.Abs(determinant));
    }

    // Uploads run OUT of the render pass: PreRender is recorded from BeginDraw's beforeRenderPass hook. Each contour puts
    // what changed into this frame's slot.
    public override void PreRender()
    {
        RefitToWindow();
        foreach (var c in _contours)
        {
            var points = c.PointsBuffer.Acquire(c.PointsBytes, out var writePoints);
            if (writePoints) points.SetData(c.PointsData, 0, (uint)c.PointsData.Length);
            if (c.Batched)
            {
                var table = c.ContoursBuffer.Acquire((ulong)(c.ContoursData.Length * sizeof(uint)), out var writeTable);
                if (writeTable) table.SetData(c.ContoursData, 0, (uint)c.ContoursData.Length);
                var owners = c.PointContoursBuffer.Acquire((ulong)(c.PointContoursData.Length * sizeof(uint)),
                    out var writeOwners);
                if (writeOwners) owners.SetData(c.PointContoursData, 0, (uint)c.PointContoursData.Length);
            }

            var arcs = c.ArcsBuffer.Acquire((ulong)(c.ArcsData.Length * sizeof(float)), out var writeArcs);
            if (writeArcs) arcs.SetData(c.ArcsData, 0, (uint)c.ArcsData.Length);
            if (c.PatternBuffer != null)
            {
                var dashes = c.PatternBuffer.Acquire((ulong)(c.PatternData.Length * sizeof(float)), out var writeDashes);
                if (writeDashes) dashes.SetData(c.PatternData, 0, (uint)c.PatternData.Length);
            }
        }
    }

    public override void Render()
    {
        if (_contours.Count == 0) return;

        // Points are in geometry-local space: WVP = transform(local->world) * projection(world->clip). The pen color
        // and transform are shared by every contour, so set them once, then draw each contour's expanded ribbon.
        _effect.Projection.SetValue(RenderData.TransformMatrix * RenderData.ProjectionMatrix);
        // StrokePS reads these for the analytic-AA coverage (distance vs HalfThickness +/- Fringe/2).
        var half = HalfThickness();
        _effect.UHalfThickness.SetValue(half);
        _effect.UFringe.SetValue(ComputeFringe());
        _effect.UDashScale.SetValue(DashUnitScale());
        if (!SetBrush(half, out var draw, out var unionDraw, out var opaque, out var invisible)) return;

        _device.VertexType = null;   // the stroke is read by address
        _device.PolygonMode = PolygonMode.Fill;
        _device.PrimitiveTopology = PrimitiveTopology.TriangleList;
        _device.ColorBlendEquation = ColorBlendEquations.AlphaBlend;
        _device.DepthTestEnabled = true;

        // An OPAQUE stroke cannot see its own overlaps, and a fully transparent one has nothing to show; both keep the
        // single cheap pass. A translucent one blends every self-crossing twice and comes out veined, so it goes
        // through the union path below instead. The pen can name either outright.
        var onePass = _pen.Overlap switch
        {
            PenOverlap.Blend => true,
            PenOverlap.Union => false,
            _ => opaque,
        };
        if (onePass || invisible || !_device.HasDepthAttachment || !TryDeviceBounds(out var bounds))
        {
            _device.DepthCompareFunction = CompareOp.Always;
            _device.DepthWriteEnable = true;
            DrawContours(draw);
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
        DrawContours(_effect.StrokeUnionDepthPass);

        _device.ColorComponentFlags = AllColorComponents;
        _device.DepthCompareFunction = CompareOp.Equal;
        _device.DepthWriteEnable = false;
        DrawContours(unionDraw);

        _device.DepthCompareFunction = CompareOp.Always;
        _device.DepthWriteEnable = true;
    }

    /// <summary>Whether a <see cref="StrokeBatchCollector"/> can draw this stroke: a brush of one color - a gradient, a
    /// pattern or a picture is shaded per stroke - under a transform with no perspective.</summary>
    internal bool CanBatch
    {
        get
        {
            if (IsDisposed || _contours.Count == 0 || !TryFlatColor(_pen.Brush, out _)) return false;
            var t = RenderData?.TransformMatrix ?? Matrix4x4F.Identity;
            return t.M14 == 0f && t.M24 == 0f;
        }
    }

    /// <summary>How far past its points the ribbon can reach, in its local units: a miter's point up to its limit in
    /// half-widths of the feathered ribbon - four at least, for the caps - and the feather on top.</summary>
    internal float LocalReach
    {
        get
        {
            var fringe = ComputeFringe();
            return Math.Max(4f, _joinType == 0u ? MiterLimitValue : 0f) * (HalfThickness() + fringe * 0.5f) + 2f * fringe;
        }
    }

    /// <summary>How many records it takes: one a draw - a batch of contours, or a contour standing alone.</summary>
    internal int RecordCount => _contours.Count;

    /// <summary>Writes into <paramref name="record"/> what draw <paramref name="draw"/> of this stroke is - its buffers as
    /// this frame reads them, its pen and its color at the element's own <paramref name="opacity"/> - leaving where it
    /// stands to the caller. <paramref name="union"/> says whether it blends its overlaps once (two passes): a stroke that
    /// shows through, or one <paramref name="faded"/> by an opacity slot, which can show through without a walk. False when
    /// it draws nothing now: gone, transparent, or no longer of one color.</summary>
    internal bool Describe(int draw, float opacity, bool faded, ref StrokeRecord record, out bool union)
    {
        union = false;
        if (IsDisposed || draw >= _contours.Count || !TryFlatColor(_pen.Brush, out var flat)) return false;

        var color = flat.ToVector4();
        color.W *= (float)_pen.Brush.Opacity * opacity;
        if (color.W <= 0f) return false;

        union = _pen.Overlap switch
        {
            PenOverlap.Blend => false,
            PenOverlap.Union => true,
            _ => color.W < 1f || faded,
        };

        var c = _contours[draw];
        record.Points = c.PointsBuffer.Acquire(c.PointsBytes, out _).GetDeviceAddress();
        record.Arcs = ArcsAddress(c);
        record.Contours = c.Batched ? ContoursAddress(c) : 0;
        record.PointContours = c.Batched ? PointContoursAddress(c) : 0;
        record.Pattern = PatternAddress(c);
        record.PointCount = c.PointCount;
        record.PatternCount = PatternCountValue;
        record.Flags = (c.IsClosed ? StrokeRecord.Closed : 0u) | (c.Batched ? StrokeRecord.BatchedContours : 0u) |
                       (HasRuns ? StrokeRecord.HasRuns : 0u) |
                       (_pen.ThicknessUnit == PenThicknessUnit.Window ? StrokeRecord.WindowThickness : 0u);
        record.Caps = MapCap(_pen.StartLineCap) | MapCap(_pen.EndLineCap) << 4 | MapCap(_pen.DashStartCap) << 8 |
                      MapCap(_pen.DashEndCap) << 12;
        record.RoundSegments = c.RoundSegments;
        record.JoinType = _joinType;
        record.Extra = new Vector4F(record.Extra.X, record.Extra.Y, MiterLimitValue, record.Extra.W);
        record.Pen = new Vector4F((float)_pen.Thickness, (float)Math.Max(_pen.MinimumDeviceThickness, 0.0),
            DashOffsetOf(c), record.Pen.W);
        record.Window = new Vector4F(TrimStartValue, TrimEndValue, record.Window.Z, record.Window.W);
        record.Color = color;
        return true;
    }

    /// <summary>Whether its brush flows with the shared noise clock.</summary>
    internal bool DrawsMoving => _pen.Brush is NoiseBrush { Animate: true };

    /// <summary>Where a picture brush finds its texture; none, and a picture stroke draws nothing.</summary>
    internal IResourceFactory ResourceFactory { get; set; }

    // Puts the pen's brush into the effect and picks the passes that paint it. False when there is nothing to draw this
    // frame - a picture still decoding, or a brush that paints nothing here.
    private bool SetBrush(float half, out IEffectPass draw, out IEffectPass unionDraw, out bool opaque, out bool invisible)
    {
        var brush = _pen.Brush;
        draw = _effect.StrokeDrawPass;
        unionDraw = _effect.StrokeUnionDrawPass;
        opaque = false;
        invisible = false;
        _effect.BrushAlong.SetValue(_pen.BrushMapping == PenBrushMapping.Along ? 1u : 0u);
        _effect.BrushBounds.SetValue(_localBounds);
        _gradient.Clear();

        // Color alpha x pen-brush Opacity x element Opacity; a gradient's stops and a pattern's colors carry the same
        // product.
        var alpha = (float)(brush?.Opacity ?? 1.0) * RenderData.Opacity;
        invisible = alpha <= 0f;
        switch (brush)
        {
            case GradientBrush gradient:
                _gradient.Set(gradient, alpha, _localBounds);
                opaque = !GradientUniforms.IsTranslucent(gradient, alpha);
                return true;

            case PatternBrush or NoiseBrush when PatternBrushRecord.TryDescribe(brush, out var record):
                SetPattern(record, alpha, out opaque);
                draw = _effect.StrokePatternDrawPass;
                unionDraw = _effect.StrokePatternUnionDrawPass;
                return true;

            case TileBrush picture:
                if (!SetPicture(picture, alpha, half, out var layer)) return false;
                draw = layer ? _effect.StrokeLayerDrawPass : _effect.StrokeTextureDrawPass;
                unionDraw = layer ? _effect.StrokeLayerUnionDrawPass : _effect.StrokeTextureUnionDrawPass;
                return true;   // what the picture holds is not known here: never taken for opaque
        }

        if (!TryFlatColor(brush, out var flat)) return false;
        var color = flat.ToVector4();
        color.W *= alpha;
        _effect.UStrokeColor.SetValue(color);
        opaque = color.W >= 1f;
        invisible = color.W <= 0f;
        return true;
    }

    // A pattern or noise, as the fill batches pack it; the cell stays in local units, as the ribbon is drawn in them.
    private void SetPattern(PatternBrushRecord record, float elementAlpha, out bool opaque)
    {
        var alpha = (float)record.Opacity * RenderData.Opacity;
        var c1 = record.Color1.ToVector4(); c1.W *= alpha;
        var c2 = record.Color2.ToVector4(); c2.W *= alpha;
        var c3 = record.MidColor.ToVector4(); c3.W *= alpha;
        _effect.PParams.SetValue(new Vector4F(0, record.Type, (float)record.Cell, 0));
        _effect.PColor1.SetValue(c1);
        _effect.PColor2.SetValue(c2);
        _effect.PColor3.SetValue(c3);
        _effect.PNoise.SetValue(record.Noise);
        _effect.PAnim.SetValue(new Vector4F((float)record.PhaseOffset, (float)record.FrozenPhase, 0, 0));
        _effect.Time.SetValue((float)NoiseClock.Time);   // an animated noise reads the shared flow clock
        opaque = elementAlpha > 0f && c1.W >= 1f && c2.W >= 1f && (c3.W <= 0f || c3.W >= 1f);
    }

    // A picture laid out by ImageTiling over the brush's box: the whole stroke's bounds - the shape's, grown by the half
    // width the ribbon reaches past them, or a single copy would leave the stroke's outer half bare - or, along the line,
    // one copy at the stroke's width and the picture's own proportions, repeated end to end.
    private bool SetPicture(TileBrush picture, float alpha, float half, out bool layer)
    {
        layer = picture.ContentSource is ImageLayer;
        var along = _pen.BrushMapping == PenBrushMapping.Along;
        var width = Math.Max(2.0 * half, 1e-4);
        var content = picture.ContentSize;
        if (content.Width <= 0 || content.Height <= 0) return false;

        var length = along ? width * content.Width / content.Height : 0.0;
        var reach = half + ComputeFringe();
        var box = along
            ? new Rect(0, 0, length, width)
            : new Rect(_localBounds.X - reach, _localBounds.Y - reach, _localBounds.Z + 2 * reach, _localBounds.W + 2 * reach);
        var texture = TextureBatchCollector.BrushTexture(picture, ResourceFactory, box.Size, Owner, layers: layer);
        if (texture == null) return false;

        _effect.BrushBounds.SetValue(new Vector4F((float)box.X, (float)box.Y, (float)box.Width, (float)box.Height));
        var world = RenderData.TransformMatrix;
        var layout = ImageTiling.Layout(picture, box, world.M11, world.M22, TextureBatchCollector.SourceIsSlice(picture));
        var tint = picture.Tint.ToVector4();
        tint.W *= alpha;

        _effect.TTile.SetValue(layout.Tile);
        _effect.TRotation.SetValue(layout.Rotation);
        _effect.TDrawn.SetValue(layout.Drawn);
        _effect.TUvRect.SetValue(layout.UvRect);
        _effect.TTint.SetValue(tint);
        _effect.TAdjust.SetValue(picture.ColorAdjustment);
        // Along the line the copies always follow each other; across it, and over the bounds, the brush's own tiling says.
        var repeats = layout.Repeats ? 1f : 0f;
        _effect.TRepeat.SetValue(new Vector2F(along ? 1f : repeats, repeats));
        _effect.TMirror.SetValue(layout.Mirror);
        _effect.TAlongLength.SetValue((float)length);
        if (picture.ContentSource is ImageLayer arrayLayer)
        {
            _effect.SourceTextureArray.SetResource(texture);
            _effect.TLayer.SetValue((float)arrayLayer.Index);
        }
        else
        {
            _effect.SourceTexture.SetResource(texture);
        }
        _effect.SourceSampler.SetResource(SamplerStates.LinearClampToEdge);
        return true;
    }

    // The one color a brush with no form along a line is drawn in: its main color, or a fractal's two blended. False for
    // a brush that paints nothing here (CurrentColor outside a drawing).
    private static bool TryFlatColor(Brush brush, out Color color)
    {
        switch (brush)
        {
            case SolidColorBrush solid: color = solid.Color; return true;
            case FractalBrush fractal: color = Halfway(fractal.Color1, fractal.Color2); return true;
            case MaterialBrush material: color = material.TintColor; return true;
            case NineSliceBrush nineSlice: color = nineSlice.Tint; return true;
            case InkBrush ink: color = ink.Color; return true;
            case CanvasGridBrush grid: color = grid.Color; return true;
            case CanvasArrowBrush arrow: color = arrow.Color; return true;
            default: color = default; return false;
        }

        static Color Halfway(Color a, Color b) => new((byte)((a.R + b.R) / 2), (byte)((a.G + b.G) / 2),
            (byte)((a.B + b.B) / 2), (byte)((a.A + b.A) / 2));
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

    // A contour's buffers are read by address, set before its pass is applied.
    private void DrawContours(IEffectPass pass)
    {
        _effect.UJoinType.SetValue(_joinType);
        _effect.UMiterLimit.SetValue(MiterLimitValue);
        _effect.UStartCap.SetValue(MapCap(_pen.StartLineCap));
        _effect.UEndCap.SetValue(MapCap(_pen.EndLineCap));
        // The two ends of a dash are separate properties: one dash can be a concave bite and a convex tip - an arrow.
        _effect.UDashStartCap.SetValue(MapCap(_pen.DashStartCap));
        _effect.UDashEndCap.SetValue(MapCap(_pen.DashEndCap));
        _effect.UTrimStart.SetValue(TrimStartValue);
        _effect.UTrimEnd.SetValue(TrimEndValue);
        _effect.URuns.SetValue(HasRuns ? 1u : 0u);
        _effect.UPatternCount.SetValue(PatternCountValue);

        foreach (var c in _contours)
        {
            if (c.PatternBuffer != null) _effect.UPattern.SetValue(PatternAddress(c));
            if (_hasDashes) _effect.UDashOffset.SetValue(DashOffsetOf(c));

            _effect.UPoints.SetValue(c.PointsBuffer.Acquire(c.PointsBytes, out _).GetDeviceAddress());
            _effect.UArcs.SetValue(ArcsAddress(c));
            _effect.UPointCount.SetValue(c.PointCount);
            _effect.UIsClosed.SetValue(c.IsClosed ? 1u : 0u);
            _effect.URoundSegments.SetValue(c.RoundSegments);
            _effect.UBatched.SetValue(c.Batched ? 1u : 0u);
            if (c.Batched)
            {
                _effect.UContours.SetValue(ContoursAddress(c));
                _effect.UPointContours.SetValue(PointContoursAddress(c));
            }

            pass.Apply();
            _device.Draw(c.VertexCount, 1);
        }
    }

    private float TrimStartValue => (float)Math.Clamp(_pen.TrimStart, 0.0, 1.0);

    private float TrimEndValue => (float)Math.Clamp(_pen.TrimEnd, 0.0, 1.0);

    // The ends are the runs' whenever there is more to them than the contour's own: a pattern or a trim window.
    private bool HasRuns => _hasDashes || _pen.TrimStart > 0.0 || _pen.TrimEnd < 1.0;

    private uint PatternCountValue => _hasDashes ? (uint)_pen.Dashes.Count : 0u;

    // The phase is in periods. A fitted contour's pattern is stretched by its own scale, and the offset and phase go with
    // it, or the ring, seamless in shape, would still drift in phase as it marches.
    private float DashOffsetOf(Contour c) =>
        _hasDashes ? (float)(_pen.DashOffset * c.DashScale + _pen.DashPhase * DashPeriod(_pen, c.DashScale)) : 0f;

    private static ulong PatternAddress(Contour c) =>
        c.PatternBuffer?.Acquire((ulong)(c.PatternData.Length * sizeof(float)), out _).GetDeviceAddress() ?? 0;

    private static ulong ArcsAddress(Contour c) =>
        c.ArcsBuffer.Acquire((ulong)(c.ArcsData.Length * sizeof(float)), out _).GetDeviceAddress();

    private static ulong ContoursAddress(Contour c) =>
        c.ContoursBuffer.Acquire((ulong)(c.ContoursData.Length * sizeof(uint)), out _).GetDeviceAddress();

    private static ulong PointContoursAddress(Contour c) =>
        c.PointContoursBuffer.Acquire((ulong)(c.PointContoursData.Length * sizeof(uint)), out _).GetDeviceAddress();
}
