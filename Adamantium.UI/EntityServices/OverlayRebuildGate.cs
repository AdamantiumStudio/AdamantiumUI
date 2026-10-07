using System;
using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.UI.Core;

namespace Adamantium.UI.EntityServices;

// Decides whether an overlay stage (popups, adorners) could differ from last frame, so an idle stage skips its rebuild.
internal sealed class OverlayRebuildGate
{
    private HashSet<Guid> _prevIds = new();
    private HashSet<Guid> _ids = new();   // swapped with _prevIds, so asking costs no allocation
    private readonly Dictionary<Guid, Vector3F> _prevPos = new();

    /// <summary>The glyph-arrival version this stage has already redrawn for. Per GATE: each overlay stage decides its
    /// own redraws, and the arrival is global.</summary>
    private int _seenGlyphVersion;

    private long _seenPaintMarks;

    private double _seenRenderScale;

    public bool HasChanged(IReadOnlyList<IUIComponent> flat, RenderDirtyScope scope, double renderScale)
    {
        var changed = false;

        // Baked at the window's scale: a move to a monitor of another DPI changes nothing else checked here.
        if (renderScale != _seenRenderScale)
        {
            _seenRenderScale = renderScale;
            changed = true;
        }

        // Recolors (opacity, brush pulses) change nothing else checked here. A monotonic counter, since the loop thread
        // clears the mark sets before this render-thread build runs.
        var marks = scope?.TotalPaintMarks ?? 0;
        if (marks != _seenPaintMarks)
        {
            _seenPaintMarks = marks;
            changed = true;
        }

        // Late-rasterized glyphs also need a rebuild: BuildFromComponents never runs the content path's late-glyph
        // adoption.
        var landed = Adamantium.Graphics.Fonts.FontAtlasStore.LandedVersion;
        if (landed != _seenGlyphVersion)
        {
            _seenGlyphVersion = landed;
            changed = true;
        }

        _ids.Clear();
        foreach (var component in flat)
        {
            _ids.Add(component.RenderId);
            if (!component.IsGeometryValid) changed = true;

            var position = component.WorldTransform.TranslationVector;
            if (_prevPos.TryGetValue(component.RenderId, out var previous) && previous.Equals(position)) continue;

            changed = true;
            _prevPos[component.RenderId] = position;
        }

        if (!changed && !_ids.SetEquals(_prevIds)) changed = true;

        if (changed)
        {
            // Forget what is no longer shown, or a stage that opens and closes things keeps growing a table of positions
            // nobody will ask about again.
            if (_prevPos.Count > _ids.Count)
            {
                _gone.Clear();
                foreach (var id in _prevPos.Keys)
                    if (!_ids.Contains(id)) _gone.Add(id);
                foreach (var id in _gone) _prevPos.Remove(id);
            }

            (_prevIds, _ids) = (_ids, _prevIds);
        }

        return changed;
    }

    private readonly List<Guid> _gone = new();
}
