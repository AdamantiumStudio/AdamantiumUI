using System;
using System.Collections.Generic;
using Adamantium.Graphics.Core;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Rendering.Payloads;
using Adamantium.UI.Rendering.RenderUnits;
using Adamantium.Vulkan.Core;

namespace Adamantium.UI.Rendering;

// Groups of draws inside one element (IDrawingSession.PushGroup): the walk meets their marks among the element's units
// and opens a target there, or lays one down. A group that cuts to a rectangle along the axes only narrows the scissor;
// one that changes nothing is drawn as it comes; the rest are drawn apart, masks and all, and laid down at their end.
public partial class RenderCache
{
    private readonly Stack<CommandGroup> _openGroups = new();

    // The scissor the groups open in the walk cut their draws by, in window device pixels; null while none does.
    private Rect2D _commandScissor;

    // A copy of what lies under a blending group for each depth of groups open at once: an inner group copies while the
    // outer one still needs its own.
    private readonly List<GroupBackdrop> _groupBackdrops = [];

    // The recorded stream holds groups whose regions were worked out from where things stood.
    private bool _opsHoldCommandGroups;

    // The groups the last walk cut to a rectangle or left out: what a replay cannot follow by placing them again - one
    // that turned, one that came into view.
    private readonly List<CommandGroup> _groupsCut = [];
    private readonly List<CommandGroup> _groupsLeftOut = [];

    // The units the last walk passed over with a group it left out, out of sight or clear.
    private readonly HashSet<Core.Graphics.IRenderUnit> _unitsLeftOutWithGroup = new();

    // Something the recorded groups were placed from moves this frame: a replay places each again where it now stands.
    private bool _groupsMove;

    /// <summary>Whether anything the recorded groups were placed from moves this frame - a node, a carried mover, a
    /// placement, something the compositor moves. Asked before those are carried, which forgets them.</summary>
    private void NoteGroupsMoving() =>
        _groupsMove = _opsHoldCommandGroups
                      && (_movedNodesBuf.Count > 0 || _movedOwnersBuf.Count > 0 || _movedPlacements.Count > 0
                          || _compositedOwners.Count > 0);

    /// <summary>Whether a replay or a patch can draw the recorded groups where things stand now, once the movers are
    /// carried: a group cut to a rectangle still lies along the axes, and none left out comes into view - its draws were
    /// never recorded. The window and its viewports are all that is asked about the second: a group left out inside
    /// another one's target, or a layer's, is taken as coming into view when the window alone would show it.</summary>
    private bool GroupsFollow()
    {
        if (!_groupsMove)
        {
            return true;
        }

        foreach (var state in _groupsCut)
        {
            var begin = IndexOfBegin(state);
            if (begin < 0 || !AlongTheAxes(ClipWorld(state, begin, World(state.Begin.Component))))
            {
                return false;
            }
        }

        foreach (var state in _groupsLeftOut)
        {
            var component = state.Begin.Component;
            var bounds = ResolveScissor(component, World(component), _frameScissor, out _, out _);
            if (Place(state, _frameScissor, bounds, true, out _, out _, out _) != CommandGroupMode.Skipped)
            {
                return false;
            }
        }

        return true;
    }

    // The walk meets a group's mark at `index` among the group's units: does what the mark asks and says which unit to
    // draw next - past whatever of the group is not drawn.
    private int WalkGroupMark(IGraphicsDevice device, ControlGroup group, int index, GroupMarkRenderUnit mark,
        ref Rect2D fullScissor, ref bool scissorNarrowed)
    {
        if (_recording)
        {
            group.NotBatchable("commandGroup");
            _opsHoldCommandGroups = true;
        }

        if (mark.Kind == GroupMarkKind.Begin)
        {
            return BeginGroup(device, group, index, mark, ref fullScissor, ref scissorNarrowed);
        }

        if (_openGroups.Count == 0)
        {
            return index + 1;
        }

        var open = _openGroups.Peek();
        mark.Group = open;
        if (mark.Kind == GroupMarkKind.End)
        {
            EndGroup(device, mark, ref fullScissor, ref scissorNarrowed);
            return index + 1;
        }

        if (open.Mode == CommandGroupMode.Layer)
        {
            FlushBatches(device, fullScissor, ref scissorNarrowed);
            fullScissor = ApplyGroupMark(device, mark, fullScissor);
            RecordGroupMark(mark);
        }

        return index + 1;
    }

    // Groups never reach past their element: the recording closes what it leaves open, so nothing is open here unless
    // its units came out of step with its marks - and then what is open is closed before the next element draws.
    private void EndGroupsLeftOpen(IGraphicsDevice device, ref Rect2D fullScissor, ref bool scissorNarrowed)
    {
        while (_openGroups.Count > 0)
        {
            EndGroup(device, null, ref fullScissor, ref scissorNarrowed);
        }
    }

    // With the device's other resources: a copy kept past them would be an image of a device that is gone.
    private void DisposeGroupBackdrops()
    {
        foreach (var backdrop in _groupBackdrops)
        {
            backdrop.Dispose();
        }

        _groupBackdrops.Clear();
    }

    // Before a walk or a replay: no group is open, nothing is cut by one.
    private void ResetGroups()
    {
        _openGroups.Clear();
        _contentOpen.Clear();
        _commandScissor = null;
        _catchingUp = null;
    }

    private int BeginGroup(IGraphicsDevice device, ControlGroup group, int index, GroupMarkRenderUnit mark,
        ref Rect2D fullScissor, ref bool scissorNarrowed)
    {
        var state = mark.Group ??= new CommandGroup(mark);
        state.Units = group.Units;
        state.BeginAt = index;
        state.Hidden = false;
        state.Depth = _openGroups.Count;
        state.Masks.Clear();
        state.BackdropTaken = false;
        state.OuterScissor = _commandScissor;
        state.Mode = Place(state, fullScissor, BoundsInside(fullScissor), true, out var area, out var contentAt,
            out var end);
        switch (state.Mode)
        {
            case CommandGroupMode.Skipped:
                if (_recording && contentAt >= 0 && state.Options.Opacity > 0)
                {
                    _groupsLeftOut.Add(state);
                }

                if (_recording)
                {
                    for (var i = index + 1; i < end && i < group.Units.Count; i++)
                    {
                        _unitsLeftOutWithGroup.Add(group.Units[i]);
                    }
                }

                return end;
            case CommandGroupMode.Pass:
                _openGroups.Push(state);
                return contentAt + 1;
        }

        // Copies of a clone run share the mark and its one state, which holds the last copy's place: such a stream is
        // walked every frame, never replayed.
        if (_cloneMatrix.HasValue)
        {
            _opsReplayable = false;
        }

        if (state.Mode == CommandGroupMode.Scissor)
        {
            state.Scissor = area;
            if (_recording)
            {
                _groupsCut.Add(state);
            }

            _openGroups.Push(state);
            // In a stretch of the stream of its own, so a replay can cut by it again what it cuts.
            FlushKeepingScissor(device, fullScissor, ref scissorNarrowed);
            _commandScissor = state.Scissor;
            RecordGroupMark(mark);
            return contentAt + 1;
        }

        state.Region = area;
        _openGroups.Push(state);
        FlushBatches(device, fullScissor, ref scissorNarrowed);
        fullScissor = ApplyGroupMark(device, mark, fullScissor);
        RecordGroupMark(mark);
        return index + 1;
    }

    // Flushes the batches at a group cut to a rectangle without forgetting that the device's scissor may still be narrow:
    // the flush puts it back only when something drew, and an unclipped draw after the group must still widen it.
    private void FlushKeepingScissor(IGraphicsDevice device, Rect2D fullScissor, ref bool scissorNarrowed)
    {
        var narrowed = scissorNarrowed;
        FlushBatches(device, fullScissor, ref scissorNarrowed);
        scissorNarrowed |= narrowed;
    }

    // What a group opened here may draw on: the scissor of a group around it that cuts, the target of one drawn apart.
    private Rect2D BoundsInside(Rect2D fullScissor)
    {
        var bounds = _commandScissor is { } outer ? Intersect(fullScissor, outer) : fullScissor;
        return EnclosingLayer() is { } enclosing ? Intersect(bounds, enclosing.Region) : bounds;
    }

    // How a group is drawn where things stand now, within `bounds`, and the scissor or the region that takes - the walk
    // decides by it, a replay places the group again by it (`cutAllowed` false keeps one drawn apart drawn apart). Says
    // where its content starts and the unit past its end.
    private CommandGroupMode Place(CommandGroup state, Rect2D fullScissor, Rect2D bounds, bool cutAllowed,
        out Rect2D area, out int contentAt, out int end)
    {
        area = default;
        var units = state.Units;
        var begin = IndexOfBegin(state);
        if (begin < 0)
        {
            contentAt = -1;
            end = 0;
            return CommandGroupMode.Skipped;
        }

        // Past its end mark: a group nothing of which is drawn is left out whole.
        end = ScanGroup(units, begin, out contentAt, out var content, out var clip, out var clipped, out var holdsBlend) + 1;
        state.HoldsBlend = holdsBlend;
        var options = state.Options;
        if (content.IsEmpty || contentAt < 0 || options.Opacity <= 0)
        {
            return CommandGroupMode.Skipped;
        }

        if (IsPlain(options))
        {
            return CommandGroupMode.Pass;
        }

        var world = World(state.Begin.Component);
        if (_cloneMatrix.HasValue) world *= _cloneMatrix.Value;
        if (cutAllowed && state.Begin.Payload.ClipRect is { } rect)
        {
            var clipWorld = ClipWorld(state, begin, world);
            if (AlongTheAxes(clipWorld))
            {
                area = Intersect(ToFramebufferScissor(rect.TransformToAABB(clipWorld), fullScissor), bounds);
                return area.Extent.Width == 0 || area.Extent.Height == 0
                    ? CommandGroupMode.Skipped
                    : CommandGroupMode.Scissor;
            }
        }

        // The fringe grows the ink before the window cuts it: ink wholly past an edge leaves nothing, not a strip along it.
        var fringe = FringePixels / _renderScale;
        area = Intersect(ToFramebufferScissor(content.Inflate(fringe), fullScissor), bounds);
        if (clipped)
        {
            // A mask covering nothing shows nothing of the group.
            area = clip.Width <= 0 || clip.Height <= 0
                ? new Rect2D { Offset = new Offset2D(), Extent = new Extent2D() }
                : Intersect(area, ToFramebufferScissor(clip.Inflate(fringe), fullScissor));
        }

        return area.Extent.Width == 0 || area.Extent.Height == 0 ? CommandGroupMode.Skipped : CommandGroupMode.Layer;
    }

    private void EndGroup(IGraphicsDevice device, GroupMarkRenderUnit mark, ref Rect2D fullScissor,
        ref bool scissorNarrowed)
    {
        var state = _openGroups.Peek();
        switch (state.Mode)
        {
            case CommandGroupMode.Scissor:
                FlushKeepingScissor(device, fullScissor, ref scissorNarrowed);
                _openGroups.Pop();
                _commandScissor = state.OuterScissor;
                if (mark != null)
                {
                    RecordGroupMark(mark);
                }
                else if (_recording)
                {
                    // Left open: no mark in the stream would stop a replay cutting by it.
                    _opsReplayable = false;
                }

                return;
            case CommandGroupMode.Layer:
                FlushBatches(device, fullScissor, ref scissorNarrowed);
                if (mark == null)
                {
                    // Left open: everything a mark would have done before its content, then its end - which no op in
                    // the stream does for a replay.
                    if (_recording)
                    {
                        _opsReplayable = false;
                    }

                    fullScissor = FinishGroup(device, state, fullScissor);
                    return;
                }

                fullScissor = ApplyGroupMark(device, mark, fullScissor);
                RecordGroupMark(mark);
                return;
            default:
                _openGroups.Pop();
                return;
        }
    }

    // A group still open when its element's units run out is laid down there - when its content was reached at all.
    private Rect2D FinishGroup(IGraphicsDevice device, CommandGroup state, Rect2D fullScissor)
    {
        if (state.MaskOpen)
        {
            state.MaskOpen = false;
            _offscreenLayers.EndGroupTarget(device);
            fullScissor = DrawIn(_offscreenLayers.Space);
        }

        if (_contentOpen.Contains(state))
        {
            return CloseGroup(device, state);
        }

        _openGroups.Pop();
        return fullScissor;
    }

    // The groups whose content target is open: a group left open before its content still has to be laid down.
    private readonly HashSet<CommandGroup> _contentOpen = [];

    // What the device does at a mark of a group drawn apart, the same in the walk and in a replay of it.
    private Rect2D ApplyGroupMark(IGraphicsDevice device, GroupMarkRenderUnit mark, Rect2D fullScissor)
    {
        var state = mark.Group;
        _offscreenLayers ??= new OffscreenLayers();
        switch (mark.Kind)
        {
            case GroupMarkKind.Begin:
                state.Masks.Clear();
                state.BackdropTaken = false;
                return fullScissor;
            case GroupMarkKind.MaskBegin:
                state.MaskByLuminosity = mark.Payload.MaskKind == MaskKind.Luminosity;
                var clear = state.MaskByLuminosity ? mark.Payload.MaskBackdrop : Colors.Transparent;
                var key = state.MaskKeys[Math.Min(state.Masks.Count, CommandGroup.MostMasks - 1)];
                state.MaskOpen = true;
                return DrawIn(_offscreenLayers.BeginGroupTarget(device, key, state.Region, clear, _projectionMatrix,
                    fullScissor));
            case GroupMarkKind.MaskEnd:
                state.MaskOpen = false;
                var mask = _offscreenLayers.EndGroupTarget(device);
                if (state.Masks.Count < CommandGroup.MostMasks)
                {
                    state.Masks.Add((mask, state.MaskByLuminosity));
                }

                return DrawIn(_offscreenLayers.Space);
            case GroupMarkKind.ContentBegin:
                return OpenGroupContent(device, state, fullScissor);
            default:
                return CloseGroup(device, state);
        }
    }

    private Rect2D OpenGroupContent(IGraphicsDevice device, CommandGroup state, Rect2D fullScissor)
    {
        // A cut is no group of its own: what blends inside one mixes with what lies under it, as it would uncut.
        state.StartsFromBackdrop = OnlyCuts(state.Options) && state.HoldsBlend;
        if (state.Options.BlendMode != BlendMode.Normal || state.StartsFromBackdrop)
        {
            while (_groupBackdrops.Count <= state.Depth)
            {
                _groupBackdrops.Add(new GroupBackdrop());
            }

            state.BackdropTaken = _groupBackdrops[state.Depth].Capture(device, state.Region);
        }

        _contentOpen.Add(state);
        var space = DrawIn(_offscreenLayers.BeginGroupTarget(device, state.ContentKey, state.Region, Colors.Transparent,
            _projectionMatrix, fullScissor));
        if (state.StartsFromBackdrop && state.BackdropTaken)
        {
            _offscreenLayers.FillFromBackdrop(device, _groupBackdrops[state.Depth], state.Region);
        }

        return space;
    }

    private static bool OnlyCuts(DrawingGroupOptions options) =>
        options.Mask == null && options.BlendMode == BlendMode.Normal && options.Opacity >= 1;


    private Rect2D CloseGroup(IGraphicsDevice device, CommandGroup state)
    {
        if (_openGroups.Count > 0 && ReferenceEquals(_openGroups.Peek(), state))
        {
            _openGroups.Pop();
        }

        _contentOpen.Remove(state);
        var content = _offscreenLayers.EndGroupTarget(device);
        var fullScissor = DrawIn(_offscreenLayers.Space);
        var scissor = Intersect(state.Region, state.OuterScissor is { } outer ? Intersect(fullScissor, outer) : fullScissor);
        var backdrop = state.BackdropTaken ? _groupBackdrops[state.Depth] : null;
        _offscreenLayers.LayDownGroup(device, content, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(state.Masks),
            backdrop, state.Options.BlendMode, (float)Math.Clamp(state.Options.Opacity, 0, 1), scissor,
            state.StartsFromBackdrop && state.BackdropTaken);
        return fullScissor;
    }

    private void RecordGroupMark(GroupMarkRenderUnit mark)
    {
        if (_recording)
        {
            // In a stretch of the stream of its own, as a layer's mark is: nothing recorded later is spliced across it.
            CloseLayer();
            RecordOp(new RenderOp { Kind = RenderOpKind.Group, Unit = mark, Order = _recordOrder, OrderFirst = _recordOrder });
            CloseLayer();
        }
    }

    // A replayed frame meets a recorded mark: the device does what it did when the walk recorded it - for a group that
    // moved, where it stands now. `hidden` says the group, moved out of sight, draws nothing this frame: its ops up to
    // its end are passed over.
    private Rect2D ReplayGroupMark(IGraphicsDevice device, GroupMarkRenderUnit mark, Rect2D fullScissor, out bool hidden)
    {
        hidden = false;
        var state = mark.Group;
        if (state == null)
        {
            return fullScissor;
        }

        if (mark.Kind == GroupMarkKind.Begin)
        {
            if (_groupsMove || state.Hidden)
            {
                var cut = state.Mode == CommandGroupMode.Scissor;
                if (Place(state, fullScissor, BoundsInside(fullScissor), cut, out var area, out _, out _)
                    == CommandGroupMode.Skipped)
                {
                    state.Hidden = true;
                    hidden = true;
                    return fullScissor;
                }

                if (cut) state.Scissor = area;
                else state.Region = area;
                // Back in sight: its per-unit draws were passed over while it moved, so they catch up before they draw.
                if (state.Hidden)
                {
                    state.Hidden = false;
                    _catchingUp ??= state;
                }
            }

            state.OuterScissor = _commandScissor;
            _openGroups.Push(state);
            if (state.Mode == CommandGroupMode.Scissor)
            {
                _commandScissor = state.Scissor;
                return fullScissor;
            }
        }
        else if (mark.Kind == GroupMarkKind.End && ReferenceEquals(_catchingUp, state))
        {
            _catchingUp = null;
        }

        if (mark.Kind == GroupMarkKind.End && state.Mode == CommandGroupMode.Scissor)
        {
            if (_openGroups.Count > 0 && ReferenceEquals(_openGroups.Peek(), state))
            {
                _openGroups.Pop();
            }

            _commandScissor = state.OuterScissor;
            return fullScissor;
        }

        return ApplyGroupMark(device, mark, fullScissor);
    }

    // A recorded scissor inside an open group, where the group lets it reach this frame: the walk recorded it cut by
    // where the group stood then.
    private Rect2D CutByGroups(IUIComponent owner, Rect2D fullScissor)
    {
        var scissor = ScissorIn(owner, fullScissor);
        return _commandScissor is { } cut ? Intersect(scissor, cut) : scissor;
    }

    // Whether a replay derives its recorded scissors again: in a layer's own space, or inside a group.
    private bool RederivesScissors => InOwnSpace || _openGroups.Count > 0;

    // The group a replay is drawing again after it was out of sight, until its end: its per-unit draws are brought to
    // where their elements stand, as in a layer's own space.
    private CommandGroup _catchingUp;

    // The innermost open group drawn into a target of its own, or null.
    private CommandGroup EnclosingLayer()
    {
        foreach (var open in _openGroups)
        {
            if (open.Mode == CommandGroupMode.Layer) return open;
        }

        return null;
    }

    private static bool IsPlain(DrawingGroupOptions options) =>
        options.Clip == null && options.TextClip is not { Count: > 0 } && options.Mask == null
        && options.BlendMode == BlendMode.Normal && options.Opacity >= 1;

    // Where the group's begin mark stands among its units now; -1 once it is gone.
    private static int IndexOfBegin(CommandGroup state)
    {
        var units = state.Units;
        return state.BeginAt < units.Count && ReferenceEquals(units[state.BeginAt], state.Begin)
            ? state.BeginAt
            : units.IndexOf(state.Begin);
    }

    // What takes the group's clip to the window: the element's world, after the clip's placement as this frame shows it
    // when it has one - as the placed draws' slots hold it.
    private Matrix4x4F ClipWorld(CommandGroup state, int begin, in Matrix4x4F world)
    {
        if (state.Options.ClipPlacement is not { } placement)
        {
            return world;
        }

        CatchUpClip(state.Units, begin, placement);
        return Shown(placement) * world;
    }

    // The clip is drawn with its placement in the group's first mask; a scissor passes that mask over, so the matrix it
    // was recorded with is learned here, as a walk learns it from any placed unit it draws.
    private void CatchUpClip(List<IRenderUnit> units, int begin, Placement placement)
    {
        for (var i = begin + 1; i < units.Count; i++)
        {
            var unit = units[i];
            if (unit is GroupMarkRenderUnit { Kind: GroupMarkKind.MaskEnd or GroupMarkKind.ContentBegin })
            {
                return;
            }

            if (unit is IPlacedRenderUnit { Placed.Placement: { } placed } && ReferenceEquals(placed, placement))
            {
                CatchUp(unit);
                return;
            }
        }
    }

    // A rectangle stays one on the window: moved, scaled, mirrored, or turned by a quarter.
    private static bool AlongTheAxes(in Matrix4x4F world) =>
        ((MathF.Abs(world.M12) < 1e-6f && MathF.Abs(world.M21) < 1e-6f)
         || (MathF.Abs(world.M11) < 1e-6f && MathF.Abs(world.M22) < 1e-6f))
        && world.M14 == 0f && world.M24 == 0f && world.M34 == 0f && world.M44 == 1f;

    // Walks the group whose begin mark is at `begin`: where its content starts, where it ends (past the last unit when
    // its element left it open), what its content covers, and what its masks that cut by coverage leave of it, in window
    // units - `clipped` saying whether any does; `holdsBlend` whether a group inside it blends by a mode.
    private int ScanGroup(List<IRenderUnit> units, int begin, out int contentAt, out Rect content, out Rect clip,
        out bool clipped, out bool holdsBlend)
    {
        contentAt = -1;
        content = Rect.Empty;
        clip = Rect.Empty;
        clipped = false;
        holdsBlend = false;
        var hasContent = false;
        var section = 0;   // 0 between, 1 a mask, 2 the content
        var depth = 0;
        Rect? maskInk = null;
        var maskByLuminosity = false;
        for (var i = begin + 1; i < units.Count; i++)
        {
            var unit = units[i];
            if (unit is GroupMarkRenderUnit mark)
            {
                switch (mark.Kind)
                {
                    case GroupMarkKind.Begin:
                        holdsBlend |= mark.Payload.Options.BlendMode != BlendMode.Normal;
                        depth++;
                        continue;
                    case GroupMarkKind.End when depth == 0:
                        return i;
                    case GroupMarkKind.End:
                        depth--;
                        continue;
                }

                if (depth > 0) continue;

                switch (mark.Kind)
                {
                    case GroupMarkKind.MaskBegin:
                        section = 1;
                        maskInk = null;
                        maskByLuminosity = mark.Payload.MaskKind == MaskKind.Luminosity;
                        break;
                    case GroupMarkKind.MaskEnd:
                        if (!maskByLuminosity)
                        {
                            var cut = maskInk ?? Rect.Empty;
                            clip = clipped ? clip.Intersect(cut) : cut;
                            clipped = true;
                        }

                        section = 0;
                        break;
                    case GroupMarkKind.ContentBegin:
                        contentAt = i;
                        section = 2;
                        break;
                }

                continue;
            }

            if (section == 0 || GroupInk(unit) is not { } ink) continue;

            if (section == 1)
            {
                maskInk = maskInk is { } held ? Union(held, ink) : ink;
            }
            else
            {
                content = hasContent ? Union(content, ink) : ink;
                hasContent = true;
            }
        }

        return units.Count;
    }

    // Where a unit inside a group draws, in window units.
    private Rect? GroupInk(IRenderUnit unit)
    {
        if (unit.Component == null) return null;

        var world = World(unit.Component);
        if (_cloneMatrix.HasValue) world *= _cloneMatrix.Value;
        var local = DrawnInk(unit, true, out var grow) is { } drawn
            ? drawn
            : new Rect(0, 0, ApplySnap(unit.Component).RenderSize.Width, ApplySnap(unit.Component).RenderSize.Height);
        return local.Inflate(grow).TransformToAABB(world);
    }
}
