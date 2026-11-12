using System;
using System.Collections.Generic;
using Adamantium.UI.Core.Graphics;

namespace Adamantium.UI.Rendering;

/// <summary>The clip slots one element's clips moving with a placement hold, one per clip in the order the element
/// pushes them: drawing again takes the same slots, and they go when the element stops being drawn.</summary>
internal sealed class PlacedClipSlots
{
    private readonly List<Guid> _ids = [];
    private readonly List<int> _slots = [];
    private readonly List<PlacedClip> _clips = [];

    /// <summary>How many clips have slots.</summary>
    public int Count => _slots.Count;

    /// <summary>The id the clip with <paramref name="ordinal"/> takes its slot under, made the first time.</summary>
    public Guid IdOf(int ordinal)
    {
        while (_ids.Count <= ordinal)
        {
            _ids.Add(Guid.NewGuid());
            _slots.Add(-1);
            _clips.Add(null);
        }

        return _ids[ordinal];
    }

    /// <summary>Notes that <paramref name="clip"/> holds <paramref name="slot"/>, so a frame drawn again without
    /// recording rewrites it.</summary>
    public void Hold(PlacedClip clip, int slot)
    {
        IdOf(clip.Ordinal);
        _slots[clip.Ordinal] = slot;
        _clips[clip.Ordinal] = clip;
    }

    /// <summary>Forgets the clip pushed last: the element no longer pushes that many.</summary>
    public void DropLast()
    {
        var last = _slots.Count - 1;
        _ids.RemoveAt(last);
        _slots.RemoveAt(last);
        _clips.RemoveAt(last);
    }

    public int SlotAt(int index) => _slots[index];

    public PlacedClip ClipAt(int index) => _clips[index];

    public Guid IdAt(int index) => _ids[index];
}
