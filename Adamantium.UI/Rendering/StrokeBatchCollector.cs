using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.EffectsFramework;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.FX;
using Adamantium.UI.Rendering.RenderUnits;
using Adamantium.Vulkan.Core;

namespace Adamantium.UI.Rendering;

/// <summary>
/// Strokes of one color - a map's roads, a chart's lines - drawn as a batch: a record a stroke, every record of a segment
/// under one pipeline and one set of bindings, a draw a record. The records point into the strokes' own buffers, so
/// nothing is copied; where a stroke stands rides its transform slot, so a camera that moves the slot moves the batch.
/// Translucent strokes go through the union's two passes for the whole segment - one style of one element blends once -
/// so a segment never mixes them with opaque ones, nor two colors or two elements of them.
/// </summary>
internal sealed class StrokeBatchCollector : BatchCollector<StrokeRecord>
{
    /// <summary>Kept in the record's flags for the collector alone: its segment takes the union's two passes.</summary>
    private const uint UnionFlag = 1u << 16;

    private const ColorComponentFlagBits AllColorComponents = ColorComponentFlagBits.RBit | ColorComponentFlagBits.GBit |
                                                              ColorComponentFlagBits.BBit | ColorComponentFlagBits.ABit;

    public static bool Enabled = true;

    private StrokeEffect _effect;
    private bool _effectUnavailable;

    // Who each record is described from: a record names its entry (Extra.y, counted from one). The records are described
    // again before the first draw of every frame, as a stroke's buffers move between the copies of their rings and its
    // pen can change without a walk. Always from the UNIT's stroke as it is now: one it has replaced is on its way to
    // being freed, and its buffers must never be read again.
    private readonly List<(IStrokeOnlyUnit Unit, GpuStrokeRenderComponent Stroke, int Draw, float Opacity)> _owners = [];
    private ulong _refreshedFrame = ulong.MaxValue;

    /// <summary>What a <see cref="TryAdd"/> came to.</summary>
    public enum Added
    {
        /// <summary>Its records are in the pending segment.</summary>
        Yes,

        /// <summary>It draws nothing now.</summary>
        No,

        /// <summary>Its style or element is not the pending segment's: flush, and add it again.</summary>
        Breaks,

        /// <summary>This frame's buffer is full; the next walk grows it.</summary>
        NoRoom,
    }

    /// <summary>Told when records no longer say what their strokes are - a stroke replaced, or one now needing the
    /// union's passes - so the next frame walks.</summary>
    public Action<string> OnStale { get; set; }

    // The pending segment's style: whether it takes the union, and the color and element it does it for. A union blends
    // its strokes once, which is right within one element - a map's roads of one style - and wrong between two: two
    // separate translucent lines darken where they cross.
    private bool _pendingUnion;
    private Vector4F _pendingColor;
    private IUIComponent _pendingElement;

    public StrokeBatchCollector() : base(256) { }

    /// <summary>Device address of the owning cache's transform table, which each record's slot indexes.</summary>
    public ulong TransformsAddress { get; set; }

    /// <summary>Device pixels per window pixel, for strokes measured in pixels or held to a least width.</summary>
    public float RenderScale { get; set; } = 1f;

    /// <summary>The stroke a unit draws, when that is all it draws and this batch can draw it.</summary>
    public static GpuStrokeRenderComponent StrokeOf(Core.Graphics.IRenderUnit unit) =>
        Enabled && unit is IStrokeOnlyUnit { Stroke: { CanBatch: true } stroke } ? stroke : null;

    /// <summary>Adds every draw of <paramref name="unit"/>'s <paramref name="stroke"/>, standing at
    /// <paramref name="local"/> on top of transform slot <paramref name="slot"/>, faded by opacity slot
    /// <paramref name="fadeSlot"/>.</summary>
    public Added TryAdd(IStrokeOnlyUnit unit, GpuStrokeRenderComponent stroke, Matrix4x4F local, int slot, int fadeSlot,
        int clipSlot, float opacity, Rect2D scissor, Rect bounds)
    {
        var count = stroke.RecordCount;
        if (count == 0) return Added.No;
        EnsureCpuCapacity(Count + count);
        if (!Admit(count)) return Added.NoRoom;

        var first = Count;
        var union = false;
        var color = default(Vector4F);
        var any = false;
        for (var draw = 0; draw < count; draw++)
        {
            var record = new StrokeRecord
            {
                Local = local,
                Window = new Vector4F(0f, 1f, slot, fadeSlot),
                Extra = new Vector4F(clipSlot, _owners.Count + draw + 1, 0f, 0f),
                Pen = new Vector4F(0f, 0f, 0f, (uint)(first + draw) * 7919u % 65536u),
            };

            if (stroke.Describe(draw, opacity, fadeSlot >= 0, ref record, out var drawUnion))
            {
                any = true;
                union = drawUnion;
                color = record.Color;
            }
            else
            {
                record.PointCount = 0;
            }

            Items[first + draw] = record;
        }

        if (!any) return Added.No;
        var element = (unit as Core.Graphics.IRenderUnit)?.Component;
        if (HasPending && (union != _pendingUnion ||
                           (union && (color != _pendingColor || !ReferenceEquals(element, _pendingElement)))))
        {
            return Added.Breaks;
        }

        for (var draw = 0; draw < count; draw++)
        {
            if (union) Items[first + draw].Flags |= UnionFlag;
            _owners.Add((unit, stroke, draw, opacity));
        }

        Count = first + count;
        LastFirst = first;
        _pendingUnion = union;
        _pendingColor = color;
        _pendingElement = element;
        MarkPending(scissor, bounds);
        return Added.Yes;
    }

    /// <summary>The first record the last successful <see cref="TryAdd"/> wrote.</summary>
    public int LastFirst { get; private set; }

    /// <summary>Re-places the records [<paramref name="first"/>, first + count) a stroke took - a move, which changes
    /// where it stands and how faded it is, never what it is. False when they are not records this frame issues.</summary>
    public bool Place(IGraphicsDevice device, int first, int count, Matrix4x4F local, int slot, int fadeSlot,
        int clipSlot, float opacity)
    {
        if (first < 0 || count <= 0 || first + count > IssuedExtent) return false;
        for (var i = first; i < first + count; i++)
            if ((int)Items[i].Extra.Y - 1 is var owner && (owner < 0 || owner >= _owners.Count)) return false;

        PrepareRetainedWrite(device);
        for (var i = first; i < first + count; i++)
        {
            ref var record = ref Items[i];
            record.Local = local;
            record.Window = new Vector4F(record.Window.X, record.Window.Y, slot, fadeSlot);
            record.Extra = new Vector4F(clipSlot, record.Extra.Y, record.Extra.Z, record.Extra.W);
            var owner = (int)record.Extra.Y - 1;
            _owners[owner] = _owners[owner] with { Opacity = opacity };
        }

        UploadRange(first, count);
        return true;
    }

    /// <summary>A stroke is many records and a patch re-bakes one, so a stroke that changed is drawn again by a walk.</summary>
    public override bool TryStage(Core.Graphics.IRenderUnit unit, Matrix4x4F world, int transformSlot, int ownerTag,
        int clipSlot = -1) => false;

    protected override void OnBeginFrame(IGraphicsDevice device) => _owners.Clear();

    protected override void OnDisposeGpuResources()
    {
        _effect?.Dispose();
        _effect = null;
        _owners.Clear();
    }

    protected override bool IsBlank(int first, int count)
    {
        for (var i = first; i < first + count; i++)
            if (Items[i].Extra.Y != 0f) return false;
        return true;
    }

    // Each record described again from its stroke as it is now, and what changed sent to this frame's copy.
    private void Refresh(IGraphicsDevice device)
    {
        if (_refreshedFrame == device.FrameTicket) return;
        _refreshedFrame = device.FrameTicket;

        var extent = IssuedExtent;
        var lo = -1;
        var hi = -1;
        string stale = null;
        for (var i = 0; i < extent; i++)
        {
            ref var record = ref Items[i];
            var owner = (int)record.Extra.Y - 1;
            if (owner < 0 || owner >= _owners.Count) continue;

            var before = record;
            var (unit, stroke, draw, opacity) = _owners[owner];
            var now = unit.Stroke;
            var union = (record.Flags & UnionFlag) != 0;
            if (!ReferenceEquals(now, stroke))
            {
                // Replaced since the walk: never the old one's buffers again. The new one is drawn as far as it matches,
                // and the walk the stream asks for draws it whole.
                stale ??= "strokeReplaced";
                if (now is not { CanBatch: true } || draw >= now.RecordCount ||
                    !now.Describe(draw, opacity, record.Window.W >= 0f, ref record, out _))
                {
                    record.PointCount = 0;
                }

                _owners[owner] = _owners[owner] with { Stroke = now };
            }
            else if (!stroke.Describe(draw, opacity, record.Window.W >= 0f, ref record, out var nowUnion))
            {
                record.PointCount = 0;
            }
            else if (nowUnion != union)
            {
                stale ??= "strokeUnion";   // the segment's passes are its walk's: another set needs another segment
            }

            record.Flags = record.Flags & ~UnionFlag | (union ? UnionFlag : 0u);
            if (MemoryMarshal.AsBytes(new ReadOnlySpan<StrokeRecord>(ref before))
                .SequenceEqual(MemoryMarshal.AsBytes(new ReadOnlySpan<StrokeRecord>(ref record)))) continue;

            if (lo < 0) lo = i;
            hi = i;
        }

        if (stale != null) OnStale?.Invoke(stale);
        if (lo < 0) return;
        PrepareRetainedWrite(device);
        UploadRange(lo, hi - lo + 1);
    }

    protected override void DrawSegment(IGraphicsDevice device, Buffer<StrokeRecord> buffer, uint count,
        uint firstInstance, Matrix4x4F projection)
    {
        if (!EnsureEffect(device)) return;
        Refresh(device);
        buffer = GpuBuffer;   // a refresh on a replayed frame may have moved to this frame's copy

        device.VertexType = null;
        device.PolygonMode = PolygonMode.Fill;
        device.PrimitiveTopology = PrimitiveTopology.TriangleList;
        device.PrimitiveRestartEnable = false;
        device.ColorBlendEnabled = true;
        device.ColorBlendEquation = ColorBlendEquations.AlphaBlend;
        device.DepthTestEnabled = true;

        _effect.Projection.SetValue(projection);
        _effect.Records.SetValue(buffer.GetDeviceAddress());
        _effect.Transforms.SetValue(TransformsAddress);
        var vp = device.CurrentViewports;
        _effect.ViewportSize.SetValue(vp is { Length: > 0 } ? new Vector2F(vp[0].Width, vp[0].Height) : default);
        _effect.TargetOrigin.SetValue(new Vector2F(device.TargetOrigin.X, device.TargetOrigin.Y));
        _effect.RenderScale.SetValue(RenderScale);
        _effect.AntiAlias.SetValue(AnalyticAa.Enabled ? 1u : 0u);
        _effect.IsGradient.SetValue(0);

        var union = (Items[firstInstance].Flags & UnionFlag) != 0 && device.HasDepthAttachment;
        if (!union)
        {
            device.ColorComponentFlags = AllColorComponents;
            device.DepthCompareFunction = CompareOp.Always;
            device.DepthWriteEnable = true;
            DrawRecords(device, _effect.StrokeBatchDrawPass, firstInstance, count);
            return;
        }

        // The depth under the segment's clip is scratch for its union, as a lone translucent stroke's bounds are for it:
        // every fragment of the segment lies inside the clip, so clearing it is enough.
        device.ClearDepthInRect(ScissorNow(device));
        device.ColorComponentFlags = 0;
        device.DepthCompareFunction = CompareOp.Less;
        device.DepthWriteEnable = true;
        DrawRecords(device, _effect.StrokeBatchUnionDepthPass, firstInstance, count);

        device.ColorComponentFlags = AllColorComponents;
        device.DepthCompareFunction = CompareOp.Equal;
        device.DepthWriteEnable = false;
        DrawRecords(device, _effect.StrokeBatchUnionDrawPass, firstInstance, count);

        device.DepthCompareFunction = CompareOp.Always;
        device.DepthWriteEnable = true;
    }

    // One pass applied, then a draw a record, its first instance naming the record.
    private void DrawRecords(IGraphicsDevice device, IEffectPass pass, uint first, uint count)
    {
        pass.Apply();
        for (var i = first; i < first + count; i++)
        {
            var vertices = Items[i].VertexCount;
            if (vertices > 0) device.Draw(vertices, 1, 0, i);
        }
    }

    private static Rect2D ScissorNow(IGraphicsDevice device)
    {
        var scissors = device.CurrentScissors;
        if (scissors.Length > 0 && scissors[0] != null) return scissors[0];

        var vp = device.CurrentViewports;
        return new Rect2D
        {
            Offset = new Offset2D { X = (int)vp[0].X, Y = (int)vp[0].Y },
            Extent = new Extent2D { Width = (uint)vp[0].Width, Height = (uint)vp[0].Height }
        };
    }

    // Built on the first draw, so an idle cache makes no shader objects; a failure is not retried every draw.
    private bool EnsureEffect(IGraphicsDevice device)
    {
        if (_effect != null) return true;
        if (_effectUnavailable) return false;

        try
        {
            _effect = new StrokeEffect(device);
            return true;
        }
        catch (InvalidOperationException e)
        {
            _effectUnavailable = true;
            Serilog.Log.Logger.Error(e, $"{nameof(StrokeBatchCollector)} draws nothing until its effect can be built");
            return false;
        }
    }
}
