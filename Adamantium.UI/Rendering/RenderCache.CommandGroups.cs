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

    // The recorded stream holds groups whose regions were worked out from where things stood: anything moving means a walk.
    private bool _opsHoldCommandGroups;

    /// <summary>Whether a replay or a patch may draw the recorded groups: nothing they were worked out from has moved
    /// since - no node, no carried mover, no placement, nothing the compositor moves.</summary>
    private bool GroupsHoldStill() =>
        !_opsHoldCommandGroups
        || (_movedNodesBuf.Count == 0 && _movedOwnersBuf.Count == 0 && _movedPlacements.Count == 0
            && _compositedOwners.Count == 0);

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
    }

    private int BeginGroup(IGraphicsDevice device, ControlGroup group, int index, GroupMarkRenderUnit mark,
        ref Rect2D fullScissor, ref bool scissorNarrowed)
    {
        var state = mark.Group ??= new CommandGroup(mark);
        var units = group.Units;
        // Past its end mark: a group nothing of which is drawn is left out whole.
        var end = ScanGroup(units, index, out var contentAt, out var content, out var clip, out var clipped) + 1;
        var options = state.Options;
        state.Depth = _openGroups.Count;
        state.Masks.Clear();
        state.BackdropTaken = false;
        state.OuterScissor = _commandScissor;
        if (content.IsEmpty || contentAt < 0 || options.Opacity <= 0)
        {
            state.Mode = CommandGroupMode.Skipped;
            return end;
        }

        var world = World(mark.Component);
        if (_cloneMatrix.HasValue) world *= _cloneMatrix.Value;
        if (IsPlain(options))
        {
            state.Mode = CommandGroupMode.Pass;
            _openGroups.Push(state);
            return contentAt + 1;
        }

        // Inside another group drawn apart, its target is all there is to draw on.
        var bounds = _commandScissor is { } outer ? Intersect(fullScissor, outer) : fullScissor;
        if (EnclosingLayer() is { } enclosing)
        {
            bounds = Intersect(bounds, enclosing.Region);
        }
        if (mark.Payload.ClipRect is { } rect && AlongTheAxes(world))
        {
            state.Mode = CommandGroupMode.Scissor;
            state.Scissor = Intersect(ToFramebufferScissor(rect.TransformToAABB(world), fullScissor), bounds);
            if (state.Scissor.Extent.Width == 0 || state.Scissor.Extent.Height == 0)
            {
                state.Mode = CommandGroupMode.Skipped;
                return end;
            }

            _commandScissor = state.Scissor;
            _openGroups.Push(state);
            return contentAt + 1;
        }

        var region = Intersect(Grow(ToFramebufferScissor(content, fullScissor), FringePixels), bounds);
        if (clipped)
        {
            // A mask covering nothing shows nothing of the group.
            region = clip.Width <= 0 || clip.Height <= 0
                ? new Rect2D { Offset = new Offset2D(), Extent = new Extent2D() }
                : Intersect(region, Grow(ToFramebufferScissor(clip, fullScissor), FringePixels));
        }

        if (region.Extent.Width == 0 || region.Extent.Height == 0)
        {
            state.Mode = CommandGroupMode.Skipped;
            return end;
        }

        state.Mode = CommandGroupMode.Layer;
        state.Region = region;
        // Copies of a clone run share the mark and its one state, which holds the last copy's region: such a stream is
        // walked every frame, never replayed.
        if (_cloneMatrix.HasValue)
        {
            _opsReplayable = false;
        }
        _openGroups.Push(state);
        FlushBatches(device, fullScissor, ref scissorNarrowed);
        fullScissor = ApplyGroupMark(device, mark, fullScissor);
        RecordGroupMark(mark);
        return index + 1;
    }

    private void EndGroup(IGraphicsDevice device, GroupMarkRenderUnit mark, ref Rect2D fullScissor,
        ref bool scissorNarrowed)
    {
        var state = _openGroups.Peek();
        switch (state.Mode)
        {
            case CommandGroupMode.Scissor:
                _openGroups.Pop();
                _commandScissor = state.OuterScissor;
                return;
            case CommandGroupMode.Layer:
                FlushBatches(device, fullScissor, ref scissorNarrowed);
                if (mark == null)
                {
                    // Left open: everything a mark would have done before its content, then its end.
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
        if (state.Options.BlendMode != BlendMode.Normal)
        {
            while (_groupBackdrops.Count <= state.Depth)
            {
                _groupBackdrops.Add(new GroupBackdrop());
            }

            state.BackdropTaken = _groupBackdrops[state.Depth].Capture(device, state.Region);
        }

        _contentOpen.Add(state);
        return DrawIn(_offscreenLayers.BeginGroupTarget(device, state.ContentKey, state.Region, Colors.Transparent,
            _projectionMatrix, fullScissor));
    }

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
            backdrop, state.Options.BlendMode, (float)Math.Clamp(state.Options.Opacity, 0, 1), scissor);
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

    // A replayed frame meets a recorded mark: the device does what it did when the walk recorded it.
    private Rect2D ReplayGroupMark(IGraphicsDevice device, GroupMarkRenderUnit mark, Rect2D fullScissor)
    {
        if (mark.Group == null)
        {
            return fullScissor;
        }

        if (mark.Kind == GroupMarkKind.Begin)
        {
            _openGroups.Push(mark.Group);
        }

        return ApplyGroupMark(device, mark, fullScissor);
    }

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

    private static bool AlongTheAxes(in Matrix4x4F world) =>
        MathF.Abs(world.M12) < 1e-6f && MathF.Abs(world.M21) < 1e-6f && world.M14 == 0f && world.M24 == 0f
        && world.M34 == 0f && world.M44 == 1f;

    // Walks the group whose begin mark is at `begin`: where its content starts, where it ends (past the last unit when
    // its element left it open), what its content covers, and what its masks that cut by coverage leave of it, in window
    // units - `clipped` saying whether any does.
    private int ScanGroup(List<IRenderUnit> units, int begin, out int contentAt, out Rect content, out Rect clip,
        out bool clipped)
    {
        contentAt = -1;
        content = Rect.Empty;
        clip = Rect.Empty;
        clipped = false;
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
