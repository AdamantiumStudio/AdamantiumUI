using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.UI.Core;

namespace Adamantium.UI.Rendering;

public partial class RenderCache
{
    /// <summary>The groups in paint order, as the frame verifier reads them. Pure reads of what the last draw left; call it
    /// on the drawing thread after the frame.</summary>
    internal IReadOnlyList<GroupView> DescribeGroups()
    {
        var views = new List<GroupView>(_groups.Count);
        foreach (var group in _groups)
        {
            // Only what the draw walked: a group that left the order waits to be swept and draws nothing.
            var component = group.Component ?? (group.Units.Count > 0 ? group.Units[0].Component : null);
            if (component == null || !group.InOrder)
            {
                continue;
            }

            views.Add(new GroupView(component, group.Units.Count, group.Clones?.Count ?? 0));
        }

        return views;
    }

    /// <summary>The layout the draw reads for <paramref name="component"/>, frozen from the packets.</summary>
    internal bool TryGetFrozen(IUIComponent component, out LayoutSnapshot snapshot) =>
        _applySnap.TryGetValue(component, out snapshot);

    /// <summary>Whether the frozen layout of <paramref name="component"/> was read from the live component because no
    /// packet carried it.</summary>
    internal bool IsReadLive(IUIComponent component) => _readLive.Contains(component);

    /// <summary>The world transform the draw composed for <paramref name="component"/>, if it composed one.</summary>
    internal bool TryGetComposedWorld(IUIComponent component, out Matrix4x4F world) =>
        _worldCache.TryGetValue(component, out world);

    /// <summary>The units of the groups in the paint order: a group out of it was not walked, so its units still carry the
    /// slots of the last walk that drew them.</summary>
    internal IReadOnlyList<Core.Graphics.IRenderUnit> DescribeUnits()
    {
        var units = new List<Core.Graphics.IRenderUnit>();
        foreach (var group in _groups)
        {
            if (group.InOrder) units.AddRange(group.Units);
        }

        return units;
    }

    /// <summary>The text runs the last walk placed in the glyph batch: which unit owns which instances.</summary>
    internal IReadOnlyList<(Core.Graphics.IRenderUnit Unit, int First, int Count)> DescribeTextRuns()
    {
        var runs = new List<(Core.Graphics.IRenderUnit, int, int)>(_textRunByUnit.Count);
        foreach (var (unit, run) in _textRunByUnit) runs.Add((unit, run.First, run.Count));
        return runs;
    }

    /// <summary>How far the glyph instances the batch still issues reach.</summary>
    internal int GlyphExtent => _textBatch?.IssuedExtent ?? 0;

    /// <summary>One glyph instance as the shader reads it.</summary>
    internal GlyphItem GlyphAt(int index) => _textBatch.ItemAt(index);

    /// <summary>What every slot of the transform table is held for, and by whom.</summary>
    internal Dictionary<int, (TransformTable.SlotUse Use, System.Guid Owner)> DescribeSlots() =>
        _transformTable?.DescribeSlots() ?? new();

    /// <summary>The alpha the shaders read from <paramref name="slot"/>.</summary>
    internal float SlotAlpha(int slot) => _transformTable?.AlphaAt(slot) ?? 1f;

    /// <summary>Whether <paramref name="component"/> is drawn into a layer of its own, whose opacity is applied when the
    /// layer is composited rather than through the slots.</summary>
    internal bool DrawsIntoLayer(IUIComponent component) => OpensLayer(component);
}
