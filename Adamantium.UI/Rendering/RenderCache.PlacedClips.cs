using System;
using System.Collections.Generic;
using Adamantium.Graphics.Core;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;

namespace Adamantium.UI.Rendering;

public partial class RenderCache
{
    private static readonly Vector4F NothingLeft = new(-1e6f, -1e6f, 1f, 1f);

    private readonly Dictionary<IUIComponent, PlacedClipSlots> _placedClipOwners = new();

    private int ClipSlotOf(IRenderUnit unit, Vulkan.Core.Rect2D fullScissor)
    {
        var clip = unit.RenderData?.PlacedClip;
        return clip == null
            ? RoundedClipSlot(unit.Component, fullScissor)
            : PlacedClipSlot(unit.Component, clip, fullScissor);
    }

    private ClipShape ClipShapeOf(IRenderUnit unit, Vulkan.Core.Rect2D fullScissor)
    {
        var clip = unit.RenderData?.PlacedClip;
        if (clip == null)
        {
            return RoundedClipShape(unit.Component, fullScissor);
        }

        var slot = PlacedClipSlot(unit.Component, clip, fullScissor);
        return slot >= 0 ? PlacedClipShape(unit.Component, clip, fullScissor) : default;
    }

    private void RenderInPlacedClip(IGraphicsDevice device, IRenderUnit unit, Vulkan.Core.Rect2D fullScissor)
    {
        if (device == null || unit.RenderData?.PlacedClip == null || InsideWhole(unit.Component))
        {
            unit.Render();
            return;
        }

        var current = device.CurrentScissors;
        var before = current.Length > 0 ? current[0] : fullScissor;
        var shape = ClipShapeOf(unit, fullScissor);
        unit.RenderData.RoundedClipBox = shape.Box;
        unit.RenderData.RoundedClipRadii = shape.Radii;
        var box = shape.Box;
        var left = (int)MathF.Floor(box.X);
        var top = (int)MathF.Floor(box.Y);
        var cut = Intersect(before, new Vulkan.Core.Rect2D
        {
            Offset = new Vulkan.Core.Offset2D { X = left, Y = top },
            Extent = new Vulkan.Core.Extent2D
            {
                Width = (uint)Math.Max(0, (int)MathF.Ceiling(box.X + box.Z) - left),
                Height = (uint)Math.Max(0, (int)MathF.Ceiling(box.Y + box.W) - top)
            }
        });

        device.SetScissors(cut);
        unit.Render();
        device.SetScissors(before);
    }

    private int PlacedClipSlot(IUIComponent owner, PlacedClip clip, Vulkan.Core.Rect2D fullScissor)
    {
        if (owner == null || _transformTable == null)
        {
            return -1;
        }

        if (!_placedClipOwners.TryGetValue(owner, out var slots))
        {
            slots = new PlacedClipSlots();
            _placedClipOwners[owner] = slots;
        }

        for (var heard = clip; heard != null; heard = heard.Outer)
        {
            Hear(heard.Placement, heard.RecordedMatrix, heard.RecordedVersion);
            Hold(owner, heard.Placement);
        }

        var slot = _transformTable.AcquireClipSlot(slots.IdOf(clip.Ordinal));
        var shape = PlacedClipShape(owner, clip, fullScissor);
        _transformTable.SetClip(null, slot, shape.Box, shape.Radii);
        slots.Hold(clip, slot);

        KeepSlotLive(_frameDevice, slot);
        return _transformTable.IsSlotLive(slot) ? slot : -1;
    }

    private ClipShape PlacedClipShape(IUIComponent owner, PlacedClip clip, Vulkan.Core.Rect2D fullScissor)
    {
        var world = World(owner);
        var box = DeviceShapeOf(clip, world).Box;
        for (var outer = clip.Outer; outer != null; outer = outer.Outer)
        {
            box = Intersection(box, DeviceShapeOf(outer, world).Box);
        }

        var rounded = CurrentRoundedShape(owner, fullScissor);
        if (rounded.Box.Z > 0)
        {
            box = Intersection(box, rounded.Box);
        }

        if (!InsideWhole(owner))
        {
            box = Intersection(box, new Vector4F(fullScissor.Offset.X, fullScissor.Offset.Y, fullScissor.Extent.Width,
                fullScissor.Extent.Height));
        }

        if (box == NothingLeft)
        {
            return new ClipShape(NothingLeft, Vector4F.Zero);
        }

        var radii = Vector4F.Zero;
        for (var cutter = clip; cutter != null; cutter = cutter.Outer)
        {
            radii = CornersLanding(box, DeviceShapeOf(cutter, world), radii);
        }

        if (rounded.Box.Z > 0)
        {
            radii = CornersLanding(box, rounded, radii);
        }

        return new ClipShape(box, radii);
    }

    private static Vector4F CornersLanding(Vector4F box, ClipShape cutter, Vector4F radii)
    {
        var right = box.X + box.Z;
        var bottom = box.Y + box.W;
        var cutterRight = cutter.Box.X + cutter.Box.Z;
        var cutterBottom = cutter.Box.Y + cutter.Box.W;
        var sameLeft = box.X == cutter.Box.X;
        var sameTop = box.Y == cutter.Box.Y;
        var sameRight = right == cutterRight;
        var sameBottom = bottom == cutterBottom;
        return new Vector4F(
            sameLeft && sameTop ? Math.Max(radii.X, cutter.Radii.X) : radii.X,
            sameRight && sameTop ? Math.Max(radii.Y, cutter.Radii.Y) : radii.Y,
            sameRight && sameBottom ? Math.Max(radii.Z, cutter.Radii.Z) : radii.Z,
            sameLeft && sameBottom ? Math.Max(radii.W, cutter.Radii.W) : radii.W);
    }

    private ClipShape CurrentRoundedShape(IUIComponent component, Vulkan.Core.Rect2D fullScissor)
    {
        for (var owner = component; owner != null; owner = ApplySnap(owner).RenderParent)
        {
            var snapshot = ApplySnap(owner);
            if (snapshot.ClipToBounds && snapshot.ClipRadii != Vector4F.Zero)
            {
                return new ClipShape(ClipBox(owner, snapshot, fullScissor), snapshot.ClipRadii * (float)_renderScale);
            }

            if (DrawsWhole(owner))
            {
                break;
            }
        }

        return default;
    }

    private ClipShape DeviceShapeOf(PlacedClip clip, Matrix4x4F world)
    {
        var toWorld = Shown(clip.Placement) * world;
        var rect = clip.Rect;
        var logical = rect.TransformToAABB(toWorld);
        var left = (float)Math.Floor(logical.X * _renderScale);
        var top = (float)Math.Floor(logical.Y * _renderScale);
        var box = new Vector4F(left, top, (float)Math.Ceiling(logical.Right * _renderScale) - left,
            (float)Math.Ceiling(logical.Bottom * _renderScale) - top);
        if (!(box.Z > 0) || !(box.W > 0))
        {
            return new ClipShape(NothingLeft, Vector4F.Zero);
        }

        var corners = clip.Corners;
        if (corners.TopLeft <= 0 && corners.TopRight <= 0 && corners.BottomRight <= 0 && corners.BottomLeft <= 0)
        {
            return new ClipShape(box, Vector4F.Zero);
        }

        var scaleX = MathF.Sqrt(toWorld.M11 * toWorld.M11 + toWorld.M12 * toWorld.M12);
        var scaleY = MathF.Sqrt(toWorld.M21 * toWorld.M21 + toWorld.M22 * toWorld.M22);
        var scale = MathF.Min(scaleX, scaleY) * (float)_renderScale;
        var centerX = box.X + box.Z * 0.5f;
        var centerY = box.Y + box.W * 0.5f;
        Span<float> radii = stackalloc float[4];
        PutCorner(radii, ToDevice(rect.X, rect.Y, toWorld), centerX, centerY, corners.TopLeft * scale);
        PutCorner(radii, ToDevice(rect.Right, rect.Y, toWorld), centerX, centerY, corners.TopRight * scale);
        PutCorner(radii, ToDevice(rect.Right, rect.Bottom, toWorld), centerX, centerY, corners.BottomRight * scale);
        PutCorner(radii, ToDevice(rect.X, rect.Bottom, toWorld), centerX, centerY, corners.BottomLeft * scale);
        return new ClipShape(box, new Vector4F(radii[0], radii[1], radii[2], radii[3]));
    }

    private Vector2<float> ToDevice(double x, double y, Matrix4x4F toWorld) =>
        new(((float)x * toWorld.M11 + (float)y * toWorld.M21 + toWorld.M41) * (float)_renderScale,
            ((float)x * toWorld.M12 + (float)y * toWorld.M22 + toWorld.M42) * (float)_renderScale);

    private static void PutCorner(Span<float> radii, Vector2<float> corner, float centerX, float centerY, double radius)
    {
        var left = corner.X < centerX;
        var top = corner.Y < centerY;
        var index = top ? (left ? 0 : 1) : (left ? 3 : 2);
        radii[index] = (float)radius;
    }

    private static Vector4F Intersection(Vector4F first, Vector4F second)
    {
        var left = Math.Max(first.X, second.X);
        var top = Math.Max(first.Y, second.Y);
        var right = Math.Min(first.X + first.Z, second.X + second.Z);
        var bottom = Math.Min(first.Y + first.W, second.Y + second.W);
        return right > left && bottom > top ? new Vector4F(left, top, right - left, bottom - top) : NothingLeft;
    }

    private void RefreshPlacedClipSlots(Vulkan.Core.Rect2D fullScissor)
    {
        if (_transformTable == null)
        {
            return;
        }

        foreach (var (owner, slots) in _placedClipOwners)
        {
            for (var index = 0; index < slots.Count; index++)
            {
                var clip = slots.ClipAt(index);
                var slot = slots.SlotAt(index);
                if (clip == null || slot < 0)
                {
                    continue;
                }

                var shape = PlacedClipShape(owner, clip, fullScissor);
                _transformTable.SetClip(null, slot, shape.Box, shape.Radii);
            }
        }
    }

    private void TrimPlacedClips(IUIComponent component, IReadOnlyList<IDrawCommand> commands)
    {
        if (!_placedClipOwners.TryGetValue(component, out var slots))
        {
            return;
        }

        var kept = 0;
        foreach (var command in commands)
        {
            for (var clip = command.RenderData?.PlacedClip; clip != null; clip = clip.Outer)
            {
                kept = Math.Max(kept, clip.Ordinal + 1);
            }
        }

        while (slots.Count > kept)
        {
            _transformTable?.ReleaseClipSlot(slots.IdAt(slots.Count - 1));
            slots.DropLast();
        }

        if (slots.Count == 0)
        {
            _placedClipOwners.Remove(component);
        }
    }

    private int ReleasePlacedClips(Func<IUIComponent, bool> isGone)
    {
        List<IUIComponent> gone = null;
        foreach (var owner in _placedClipOwners.Keys)
        {
            if (isGone(owner))
            {
                (gone ??= []).Add(owner);
            }
        }

        if (gone == null)
        {
            return 0;
        }

        foreach (var owner in gone)
        {
            var slots = _placedClipOwners[owner];
            for (var index = 0; index < slots.Count; index++)
            {
                _transformTable?.ReleaseClipSlot(slots.IdAt(index));
            }

            _placedClipOwners.Remove(owner);
        }

        return gone.Count;
    }
}
