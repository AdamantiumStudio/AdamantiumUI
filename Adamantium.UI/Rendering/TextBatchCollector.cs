using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Rendering.RenderUnits;
using Adamantium.Vulkan.Core;

namespace Adamantium.UI.Rendering;

// Text glyphs of same-clip, same-atlas blocks in one instanced draw per segment; glyphs are node-local and transformed on
// the GPU by their slot, with per-instance color. Drawn above the rect batch.
internal sealed class TextBatchCollector : BatchCollector<GlyphItem>
{
    private FontAtlas _atlas;            // the pending segment's atlas (one bind per draw)
    private FontRenderer _fontRenderer;

    /// <summary>Device address of the owning cache's transform table - the glyph VS fetches each instance's node matrix
    /// from it by the instance's slot (set by RenderCache every frame; slot 0 is identity for world-baked glyphs).</summary>
    public ulong TransformsAddress { get; set; }


    // Per-segment atlas + renderer, parallel to the base segment list, so the clean-frame op replay can re-bind each
    // recorded segment's atlas (DrawSegment reads _atlas/_fontRenderer, which otherwise hold only the LAST segment's).
    private readonly List<(FontAtlas Atlas, FontRenderer Renderer)> _segState = new();

    public TextBatchCollector() : base(8192) { }

    protected override void OnBeginFrame(IGraphicsDevice device) => _segState.Clear();

    // The atlas the glyphs staged for this patch need. A text segment binds ONE atlas, so a repair that would put glyphs
    // of another one into it is refused rather than drawn with the wrong sheet.
    private FontAtlas _stagedAtlas;
    private FontRenderer _stagedRenderer;

    /// <summary>Bake a block's glyphs into the patch stage - the text answer to BatchArena.TryStage. This is the half a
    /// changed glyph COUNT needs: the in-place re-bake (UpdateRun) only ever covered a count that held steady, and
    /// anything that grew or shrank - a counter, a clock, an fps plate - fell through to a walk of the whole scene.</summary>
    public override bool TryStage(IRenderUnit unit, Matrix4x4F world, int transformSlot, int ownerTag, int clipSlot = -1)
    {
        if (unit is not TextRenderUnit tru || tru.TextComponent is not { } tc) return false;
        if (!CanBatch(tc, out var atlas)) return false;
        if (_stagedAtlas != null && atlas != _stagedAtlas) return false;   // one patch, one sheet

        // The unit's OWN placement on top of the bake - a Drawing's text run sits at its own spot inside the element.
        // The walk folds it in the same way; without it the glyphs land at the element's origin instead of the run's.
        var placed = tru.Place(world);

        var first = Stage.Count;
        for (var i = 0; i < tc.GlyphRun.Count; i++) Stage.Add(default);
        if (!PackInto(tc, placed, transformSlot, unit.FadeSlot, clipSlot,
                CollectionsMarshal.AsSpan(Stage).Slice(first, tc.GlyphRun.Count)))
        {
            Stage.RemoveRange(first, Stage.Count - first);
            return false;
        }

        _stagedAtlas = atlas;
        _stagedRenderer = tc.FontRenderer;
        return true;
    }

    public override void ClearStage()
    {
        base.ClearStage();
        _stagedAtlas = null;
        _stagedRenderer = null;
    }

    // A repaired segment keeps drawing with the sheet it was recorded against, so the staged glyphs have to belong to it.
    private bool SegmentTakesStagedAtlas(int id)
    {
        var index = IndexOfSegment(id);
        if (index < 0 || index >= _segState.Count) return false;
        return _segState[index].Atlas == _stagedAtlas;
    }

    public override bool ReplaceStagedInSegment(IGraphicsDevice device, int id, int at, int replaced, int stageFirst, int stageCount)
        => SegmentTakesStagedAtlas(id) && base.ReplaceStagedInSegment(device, id, at, replaced, stageFirst, stageCount);

    public override bool RepointSegmentAroundStage(IGraphicsDevice device, int id, int first, int at, int replaced, int count,
        Rect2D scissor, int stageFirst, int stageCount)
        => SegmentTakesStagedAtlas(id)
           && base.RepointSegmentAroundStage(device, id, first, at, replaced, count, scissor, stageFirst, stageCount);

    public override int AllocateSegmentFromStage(IGraphicsDevice device, Rect2D scissor, int stageFirst, int stageCount)
    {
        // A brand-new segment has no sheet yet - it takes the staged one, which is what OnSegmentRecorded would have
        // written had a walk produced it.
        _atlas = _stagedAtlas;
        _fontRenderer = _stagedRenderer;
        return base.AllocateSegmentFromStage(device, scissor, stageFirst, stageCount);
    }

    protected override void OnSegmentRecorded(int index)
    {
        while (_segState.Count <= index) _segState.Add(default);
        _segState[index] = (_atlas, _fontRenderer);
    }

    protected override void OnSegmentInserted(int index)
    {
        while (_segState.Count < index) _segState.Add(default);
        _segState.Insert(index, index > 0 ? _segState[index - 1] : default);
    }

    protected override void BindSegment(int index)
    {
        var s = _segState[index];
        _atlas = s.Atlas;
        _fontRenderer = s.Renderer;
    }

    // Whether this block can batch at all (and its atlas). Canonical MSDF only - the batch pixel shader is the MSDF
    // variant; outline / gradient-AA / empty / non-solid-foreground text (and UseTextBatch=off) fall back to the
    // per-block direct draw. The clip-group check lives in RenderCache; the atlas check is SameAtlas below.
    public bool CanBatch(TextRenderComponent tc, out FontAtlas atlas)
    {
        atlas = null;
        if (!FontRenderer.UseTextBatch) return false;   // off -> every block falls back to the per-block direct draw
        var run = tc.GlyphRun;                            // the FROZEN glyph snapshot (not the live, reshaped-in-place layout)
        if (run == null || run.Count == 0 || run.Atlas == null) return false;
        if (tc.FontRenderer == null) return false;
        if (tc.Foreground is not SolidColorBrush) return false;
        atlas = run.Atlas;
        return true;
    }

    // Still the pending segment's atlas? (One draw binds one atlas; a change flushes both batches - see RenderCache.)
    public bool SameAtlas(FontAtlas atlas) => !Active || _atlas == atlas;

    // Packs a block's glyphs with the node-relative scale/translate and slot; false on a rotated relative transform or
    // overflow, and the caller draws the block directly.
    public bool TryAdd(TextRenderComponent tc, Matrix4x4F relWorld, int transformSlot, int fadeSlot, Rect2D scissor, FontAtlas atlas,
        Rect logicalBounds, int clipSlot = -1)
    {
        var n = tc.GlyphRun.Count;

        EnsureCpuCapacity(Count + n);
        if (Count + n > GpuCapacity) return false;   // won't fit this frame's GPU buffer -> direct
        if (!Pack(tc, relWorld, transformSlot, fadeSlot, clipSlot, Count)) return false;

        Count += n;
        _atlas = atlas;
        _fontRenderer = tc.FontRenderer;
        MarkPending(scissor, logicalBounds);
        return true;
    }

    /// <summary>Rewrites only the color of a retained run in place, so recolors work on replayed frames without a
    /// re-pack.</summary>
    public bool RecolorRun(IGraphicsDevice device, int first, int count, TextRenderComponent tc)
    {
        if (count <= 0 || tc?.Foreground is not SolidColorBrush solid) return false;
        if (first < 0 || first + count > Count) return false;

        var color = solid.Color.ToVector4();
        var opacity = (float)tc.RenderData.Opacity;
        color.W *= opacity;   // the same fold PackInto does - one color, computed one way
        var ownFade = MathF.Pow(opacity, 2.2f);

        var span = Items.AsSpan(first, count);
        var run = tc.GlyphRun;
        var changed = false;
        for (var i = 0; i < span.Length; i++)
        {
            var target = i < run.Count ? GlyphColor(run.Glyphs[i], color, opacity) : color;
            if (span[i].Color == target && span[i].Paint.Y == ownFade) continue;
            span[i].Color = target;
            span[i].Paint.Y = ownFade;
            changed = true;
        }

        if (!changed) return false;

        PrepareRetainedWrite(device);
        UploadRange(first, count);
        return true;
    }

    /// <summary>Re-bakes a flushed block into the run it already occupies, so the frame can replay; false when it no
    /// longer packs (a rotated relative transform).</summary>
    public bool UpdateRun(IGraphicsDevice device, int first, TextRenderComponent tc, Matrix4x4F relWorld, int transformSlot, int fadeSlot,
        int clipSlot = -1)
    {
        PrepareRetainedWrite(device);
        if (!Pack(tc, relWorld, transformSlot, fadeSlot, clipSlot, first)) return false;
        UploadRange(first, tc.GlyphRun.Count);
        return true;
    }

    // Write one block's glyphs at [at, at+run.Count): each glyph's LOCAL rect folded by the node-RELATIVE scale/translate
    // (the axis-aligned rect can hold that), its transform SLOT, its atlas UV, and the block's foreground as a per-instance
    // color. NO world matrix is applied here - the glyph VS applies the node matrix (from the transform table at the slot)
    // on the GPU. False (no write) for a rotated/sheared RELATIVE transform. Mirrors RectBatchCollector's bake.
    private bool Pack(TextRenderComponent tc, Matrix4x4F relWorld, int transformSlot, int fadeSlot, int clipSlot, int at)
        => PackInto(tc, relWorld, transformSlot, fadeSlot, clipSlot, Items.AsSpan(at, tc.GlyphRun.Count));

    /// <summary>Bake one block's glyphs into <paramref name="dst"/>. Where they land is the caller's business - the
    /// retained arena during a walk, the patch stage during a repair - and the bake is the same either way.</summary>
    private static bool PackInto(TextRenderComponent tc, Matrix4x4F relWorld, int transformSlot, int fadeSlot, int clipSlot,
        Span<GlyphItem> dst)
    {
        const float eps = 1e-4f;
        if (Math.Abs(relWorld.M12) > eps || Math.Abs(relWorld.M21) > eps) return false;

        var run = tc.GlyphRun;                        // FROZEN snapshot - the applier never reads the live TextLayout here
        var area = tc.RenderingParameters.TextArea;
        var color = ((SolidColorBrush)tc.Foreground).Color.ToVector4();
        var opacity = (float)tc.RenderData.Opacity;
        color.W *= opacity;                           // fold the element's opacity into the glyph alpha

        float sx = relWorld.M11, sy = relWorld.M22, tx = relWorld.M41, ty = relWorld.M42;
        float ax = (float)area.X, ay = (float)area.Y;
        var glyphs = run.Glyphs;
        for (var i = 0; i < run.Count; i++)
        {
            var d = glyphs[i].ArrangeRect;   // local x, y, w, h
            var synthesis = glyphs[i].Synthesis;
            dst[i] = new GlyphItem
            {
                LocalRect = new Vector4F((d.X + ax) * sx + tx, (d.Y + ay) * sy + ty, d.Z * sx, d.W * sy),
                Source = glyphs[i].Source,
                Params = new Vector4F(transformSlot, glyphs[i].Layer, glyphs[i].Depth, fadeSlot),
                Clip = new Vector4F(clipSlot, synthesis.X, synthesis.Y, synthesis.Z),
                Color = GlyphColor(glyphs[i], color, opacity),
                Paint = new Vector4F(glyphs[i].Paint.X, MathF.Pow(opacity, 2.2f), 0, 0)
            };
        }

        return true;
    }

    private static Vector4F GlyphColor(in FontItem glyph, Vector4F foreground, float opacity)
    {
        if (!glyph.HasOwnColor)
        {
            return foreground;
        }

        var own = glyph.Color;
        own.W *= opacity;
        return own;
    }

    protected override void DrawSegment(IGraphicsDevice device, Buffer<GlyphItem> buffer, uint count, uint firstInstance, Matrix4x4F projection)
    {
        var stride = (ulong)Marshal.SizeOf<GlyphItem>();
        _fontRenderer.DrawBatch(SamplerStates.LinearFont, _atlas,
            buffer.GetDeviceAddress() + firstInstance * stride, TransformsAddress, count, projection);
    }
}
