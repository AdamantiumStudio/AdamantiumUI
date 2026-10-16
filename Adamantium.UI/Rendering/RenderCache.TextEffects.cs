using System;
using System.Collections.Generic;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Rendering.RenderUnits;

namespace Adamantium.UI.Rendering;

// Glyph effects live in the transform table, a chain of slots per text: the glyphs carry its head, which no glyph is in,
// so an effect that changes - or one more or less - is slot writes, never a re-record or a re-pack.
public partial class RenderCache
{
    private static readonly GlyphEffect ChainHead = new(0, 0, Matrix3x3F.Identity, false, null);

    private readonly List<IUIComponent> _effectDirtyBuffer = new();

    private void CaptureEffects()
    {
        Dirty.SnapshotEffectsInto(_effectDirtyBuffer);
        foreach (var component in _effectDirtyBuffer)
        {
            if (component is not IGlyphEffectSource source)
            {
                continue;
            }

            var first = _packet.EffectValues.Count;
            source.ResolveEffects(_packet.EffectValues);
            _packet.Effects.Add((component, first, _packet.EffectValues.Count - first));
        }
    }

    private void ApplyEffects(RenderPacket packet)
    {
        foreach (var (component, first, count) in packet.Effects)
        {
            if (!_groupById.TryGetValue(component.RenderId, out var group))
            {
                continue;
            }

            foreach (var unit in group.Units)
            {
                if (unit is TextRenderUnit text)
                {
                    WriteEffects(text, packet.EffectValues, first, count, recordedWith: false);
                }
            }
        }
    }

    private void NoteEffects(IRenderUnit unit)
    {
        if (unit is TextRenderUnit { Payload: { } payload } text)
        {
            WriteEffects(text, payload.Effects, 0, payload.Effects.Count, recordedWith: true);
        }
    }

    private void WriteEffects(TextRenderUnit text, IReadOnlyList<GlyphEffect> values, int first, int count,
        bool recordedWith)
    {
        if (text.TextComponent is not { } component || text.Component == null || (count == 0 && text.EffectSlots == null))
        {
            return;
        }

        var table = EnsureTransformTable();
        var owner = text.Component.RenderId;
        var slots = text.EffectSlots ??= [];
        var clips = text.EffectClipSlots ??= [];
        var fresh = slots.Count == 0;
        while (slots.Count < count + 1)
        {
            slots.Add(table.AcquireEffectSlot(owner));
        }

        while (slots.Count > count + 1)
        {
            table.ReleaseEffectSlot(slots[^1]);
            slots.RemoveAt(slots.Count - 1);
        }

        while (clips.Count > count)
        {
            if (clips[^1] >= 0)
            {
                table.ReleaseEffectSlot(clips[^1]);
            }

            clips.RemoveAt(clips.Count - 1);
        }

        ChainHead.ToSlot(count > 0 ? slots[1] : -1, -1, out var headRows, out var headColor);
        table.SetEffect(slots[0], headRows, headColor);
        var last = slots[0];
        for (var i = 0; i < count; i++)
        {
            var effect = values[first + i];
            var clipSlot = ClipSlot(table, owner, clips, i, effect.Clip);
            effect.ToSlot(i + 1 < count ? slots[i + 2] : -1, clipSlot, out var rows, out var color);
            table.SetEffect(slots[i + 1], rows, color);
            last = Math.Max(last, Math.Max(slots[i + 1], clipSlot));
        }

        component.UseEffects(table, slots[0], last);

        // Glyphs packed before the chain was there carry no head.
        if (fresh && !recordedWith)
        {
            text.Component.InvalidateRender(false);
        }
    }

    private static int ClipSlot(TransformTable table, Guid owner, List<int> clips, int index, GlyphClip? clip)
    {
        while (clips.Count <= index)
        {
            clips.Add(-1);
        }

        if (clip is not { } shape)
        {
            if (clips[index] >= 0)
            {
                table.ReleaseEffectSlot(clips[index]);
                clips[index] = -1;
            }

            return -1;
        }

        if (clips[index] < 0)
        {
            clips[index] = table.AcquireEffectSlot(owner);
        }

        shape.ToSlot(out var rows, out var rest);
        table.SetEffect(clips[index], rows, rest);
        return clips[index];
    }

    private void ReleaseEffects(TextRenderUnit text)
    {
        if (text.EffectSlots == null)
        {
            return;
        }

        foreach (var slot in text.EffectSlots)
        {
            _transformTable?.ReleaseEffectSlot(slot);
        }

        foreach (var slot in text.EffectClipSlots)
        {
            if (slot >= 0)
            {
                _transformTable?.ReleaseEffectSlot(slot);
            }
        }

        text.EffectSlots = null;
        text.EffectClipSlots = null;
        text.TextComponent?.UseEffects(null, -1, -1);
    }
}
