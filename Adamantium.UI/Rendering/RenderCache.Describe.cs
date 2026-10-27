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
        _describedUnits.Clear();
        foreach (var group in _groups)
        {
            if (group.InOrder) _describedUnits.AddRange(group.Units);
        }

        return _describedUnits;
    }

    // Kept between frames: the verifier asks every frame it checks, and each answer is a scene-sized collection. Read only
    // on the drawing thread, until the next ask.
    private readonly List<Core.Graphics.IRenderUnit> _describedUnits = [];
    private readonly Dictionary<Core.Graphics.IRenderUnit, int> _describedOrder = new();
    private readonly Dictionary<int, Core.Graphics.IRenderUnit> _rectUnitBySlot = new();
    private readonly List<(int First, int Count, int Op)> _textSegments = [];
    private readonly List<(Core.Graphics.IRenderUnit, int, int)> _describedRuns = [];

    /// <summary>When batched fills and text are drawn in the op stream the frame issues: the index of the op that draws
    /// each. Only fills, which cover what lies under them - an outline drawn later only touches the edges. Paint order is
    /// the one thing the data of each unit cannot tell.</summary>
    internal Dictionary<Core.Graphics.IRenderUnit, int> DescribeDrawOrder()
    {
        var order = _describedOrder;
        order.Clear();
        _rectUnitBySlot.Clear();
        foreach (var (unit, slot) in _rectSlotByUnit) _rectUnitBySlot[slot] = unit;
        _textSegments.Clear();

        for (var i = 0; i < _ops.Count; i++)
        {
            var op = _ops[i];
            if (op.Kind != RenderOpKind.Segment) continue;

            if (_rectBatch != null && op.Batch == _rectBatch.BatchId && _rectBatch.TryGetSegment(op.SegId, out var first, out var count))
            {
                for (var slot = first; slot < first + count; slot++)
                {
                    if (_rectUnitBySlot.TryGetValue(slot, out var unit)) order.TryAdd(unit, i);
                }
            }
            else if (_textBatch != null && op.Batch == _textBatch.BatchId && _textBatch.TryGetSegment(op.SegId, out first, out count))
            {
                _textSegments.Add((first, count, i));
            }
        }

        // Each run finds its segment by bisection over the segments sorted by start - not every segment asking every run.
        _textSegments.Sort(static (a, b) => a.First.CompareTo(b.First));
        foreach (var (unit, run) in _textRunByUnit)
        {
            int low = 0, high = _textSegments.Count - 1, at = -1;
            while (low <= high)
            {
                var mid = (low + high) >> 1;
                if (_textSegments[mid].First <= run.First) { at = mid; low = mid + 1; }
                else high = mid - 1;
            }

            if (at >= 0 && run.First < _textSegments[at].First + _textSegments[at].Count) order.TryAdd(unit, _textSegments[at].Op);
        }

        return order;
    }

    /// <summary>The rank the draw holds for <paramref name="component"/>'s group, and the ranks op <paramref name="op"/>
    /// claims to span - what tells a wrong rank from a wrong place in the stream.</summary>
    internal string DescribeRanks(IUIComponent component, int op)
    {
        var group = _groupById.TryGetValue(component.RenderId, out var g) ? g.Order.ToString() : "none";
        var span = op >= 0 && op < _ops.Count ? $"[{_ops[op].OrderFirst}, {_ops[op].Order}] batch {_ops[op].Batch}" : "?";
        return $"rank {group}, op {op} spans {span}";
    }

    internal string DescribeTextGroup(Core.Graphics.IRenderUnit unit)
    {
        if (unit.Component == null || !_groupById.TryGetValue(unit.Component.RenderId, out var group)) return "no group";
        var runs = string.Join(" ", group.Runs.ConvertAll(r => $"[{r.First}, {r.First + r.Count})"));
        if (group.OtherRuns.Count > 0)
        {
            runs += ", in other arenas " + string.Join(" ", group.OtherRuns.ConvertAll(r =>
                $"{(ReferenceEquals(r.Arena, _textBatch) ? "text" : r.Arena.GetType().Name)} [{r.First}, {r.First + r.Count})"));
        }
        return $"group runs {runs}, walked {group.WalkVersion} of {_walkVersion}, arena {(ReferenceEquals(group.Arena, _textBatch) ? "text" : group.Arena?.GetType().Name ?? "none")}";
    }

    /// <summary>The rank the draw holds for <paramref name="component"/>'s group, or null without one.</summary>
    internal long? RankOfGroup(IUIComponent component) =>
        _groupById.TryGetValue(component.RenderId, out var group) ? group.Order : null;

    /// <summary>The text runs the last walk placed in the glyph batch: which unit owns which instances.</summary>
    internal IReadOnlyList<(Core.Graphics.IRenderUnit Unit, int First, int Count)> DescribeTextRuns()
    {
        _describedRuns.Clear();
        foreach (var (unit, run) in _textRunByUnit) _describedRuns.Add((unit, run.First, run.Count));
        return _describedRuns;
    }

    /// <summary>How far the glyph instances the batch still issues reach.</summary>
    internal int GlyphExtent => _textBatch?.IssuedExtent ?? 0;

    /// <summary>Whether a segment of the glyph batch draws the instances [first, first + count).</summary>
    internal bool GlyphsIssued(int first, int count) => _textBatch != null && _textBatch.Issues(first, count);

    /// <summary>Whether the last walk left <paramref name="unit"/> out because it lies outside its clip.</summary>
    internal bool WasCulled(Core.Graphics.IRenderUnit unit) => _culledWhenRecorded.Contains(unit);

    /// <summary>Whether text of this kind is drawn through the glyph batch rather than on its own.</summary>
    internal bool BatchesText(RenderUnits.TextRenderComponent text) => _textBatch != null && _textBatch.CanBatch(text, out _);

    /// <summary>One glyph instance as the shader reads it.</summary>
    internal GlyphItem GlyphAt(int index) => _textBatch.ItemAt(index);

    /// <summary>What every slot of the transform table is held for, and by whom.</summary>
    internal Dictionary<int, (TransformTable.SlotUse Use, System.Guid Owner)> DescribeSlots() =>
        _transformTable?.DescribeSlots() ?? new();

    /// <summary>The clip rectangle (device px) the shaders read from <paramref name="slot"/>.</summary>
    internal Vector4F SlotClipBox(int slot) => _transformTable?.ClipBoxAt(slot) ?? default;

    /// <summary>The clip rectangle a clip owner of <paramref name="size"/> at <paramref name="world"/> makes, by the same
    /// conversion the draw uses - for the verifier to feed it the tree's place.</summary>
    internal Vector4F ClipBoxOf(IUIComponent owner, Size size, Matrix4x4F world) =>
        ClipBox(owner, new Rect(0, 0, size.Width, size.Height).TransformToAABB(world), _frameScissor);

    /// <summary>The matrix the shaders read from <paramref name="slot"/>.</summary>
    internal Matrix4x4F SlotMatrix(int slot) => _transformTable?.MatrixAt(slot) ?? Matrix4x4F.Identity;

    /// <summary>The alpha the shaders read from <paramref name="slot"/>.</summary>
    internal float SlotAlpha(int slot) => _transformTable?.AlphaAt(slot) ?? 1f;

    /// <summary>Whether <paramref name="component"/> is drawn into a layer of its own, whose opacity is applied when the
    /// layer is composited rather than through the slots.</summary>
    internal bool DrawsIntoLayer(IUIComponent component) => OpensLayer(component);
}
