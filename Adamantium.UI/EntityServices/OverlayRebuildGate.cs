using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Core;

namespace Adamantium.UI.EntityServices;

// Decides whether an overlay stage (popups, adorners) could differ from last frame, so an idle stage skips its rebuild.
internal sealed class OverlayRebuildGate
{
    private readonly HashSet<Guid> _ids = new();

    // What is drawn, in the order it is drawn: one brought to the front changes what is on top, and nothing else.
    private List<Guid> _prevOrder = new();
    private List<Guid> _order = new();   // swapped with _prevOrder, so asking costs no allocation
    private readonly Dictionary<Guid, Vector3F> _prevPos = new();

    /// <summary>The glyph-arrival version this stage has already redrawn for. Per GATE: each overlay stage decides its
    /// own redraws, and the arrival is global.</summary>
    private int _seenGlyphVersion;

    private long _seenPaintMarks;

    private long _seenEffectMarks;

    private double _seenRenderScale;

    private Matrix4x4F _seenProjection;

    private long _seenReleases;

    public bool HasChanged(IReadOnlyList<IUIComponent> flat, RenderDirtyScope scope, double renderScale,
        Matrix4x4F projection)
    {
        var changed = false;

        // A window that changed size draws through another projection: what was recorded through the old one lands where
        // the window was, a focus ring a few pixels off the field it rings.
        if (!projection.Equals(_seenProjection))
        {
            _seenProjection = projection;
            changed = true;
        }

        // Elements released since the last record: the stage's cache lets go of what it kept for them only once it has
        // applied a packet recorded after their release, and an idle stage would otherwise never record one. Once the
        // queue is empty, not on every batch of a teardown that drains over many frames.
        var releases = DiscardedVisuals.ReleasedBatches;
        if (releases != _seenReleases && DiscardedVisuals.PendingCount == 0)
        {
            _seenReleases = releases;
            changed = true;
        }

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

        // ...and so do a text's glyph effects, which change without the text being drawn again.
        var effects = scope?.TotalEffectMarks ?? 0;
        if (effects != _seenEffectMarks)
        {
            _seenEffectMarks = effects;
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
        _order.Clear();
        foreach (var component in flat)
        {
            _ids.Add(component.RenderId);
            _order.Add(component.RenderId);
            if (!component.IsGeometryValid) changed = true;

            var position = component.WorldTransform.TranslationVector;
            if (_prevPos.TryGetValue(component.RenderId, out var previous) && previous.Equals(position)) continue;

            changed = true;
            _prevPos[component.RenderId] = position;
        }

        if (!changed && !_order.SequenceEqual(_prevOrder)) changed = true;

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

            (_prevOrder, _order) = (_order, _prevOrder);
        }

        return changed;
    }

    private readonly List<Guid> _gone = new();
}
