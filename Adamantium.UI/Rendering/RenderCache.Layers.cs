using System;
using System.Collections.Generic;
using Adamantium.Graphics.Core;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Rendering.RenderUnits;
using Adamantium.Vulkan.Core;

namespace Adamantium.UI.Rendering;

public partial class RenderCache
{
    private const int FringePixels = 2;
    private const double MiterReach = 2.0;
    private const double TextOverhang = 0.25;

    private readonly Stack<(int End, IUIComponent Layer)> _layerWalk = new();
    private OffscreenLayers _offscreenLayers;

    private void BeginOffscreenLayer(IGraphicsDevice device, IUIComponent layer, Rect2D fullScissor)
    {
        var start = GroupIndexOf(layer);
        BeginOffscreenLayer(device, layer, fullScissor, start, start < 0 ? -1 : CloneSubtreeEnd(start));
    }

    private void BeginOffscreenLayer(IGraphicsDevice device, IUIComponent layer, Rect2D fullScissor, int start, int end)
    {
        _offscreenLayers ??= new OffscreenLayers();
        _offscreenLayers.Begin(device, LayerRegion(layer, fullScissor, start, end));
    }

    private void CloseOffscreenLayer(IGraphicsDevice device, Rect2D fullScissor, ref bool scissorNarrowed)
    {
        var (end, layer) = _layerWalk.Pop();
        if (device != null)
        {
            FlushBatches(device, fullScissor, ref scissorNarrowed);
        }

        EndOffscreenLayer(device, layer, fullScissor);
        if (_recording)
        {
            RecordLayerMark(RenderOpKind.LayerEnd, layer, _groups[end - 1].Order + 1);
        }
    }

    private void EndOffscreenLayer(IGraphicsDevice device, IUIComponent layer, Rect2D fullScissor)
    {
        if (device == null || _offscreenLayers == null)
        {
            return;
        }

        var scissor = ResolveScissor(layer, World(layer), fullScissor, out _, out _);
        var alpha = LayerAlpha(layer);
        var mask = MaskOf(layer, fullScissor, ref alpha);
        _offscreenLayers.End(device, alpha, scissor, mask);
    }

    private LayerMask MaskOf(IUIComponent layer, Rect2D fullScissor, ref float alpha)
    {
        var brush = layer.OpacityMask?.Snapshot;
        if (brush == null)
        {
            return default;
        }

        alpha *= (float)brush.Opacity;
        if (brush is SolidColorBrush solid)
        {
            alpha *= solid.Color.ToVector4().W;
            return default;
        }

        if (brush is not GradientBrush gradient)
        {
            return default;
        }

        if (!MaskBox(layer, fullScissor, out var toBox))
        {
            return default;
        }

        var type = GradientBake.PackGeometry(gradient, out var geom0, out var geom1);
        Span<Vector4F> colors = stackalloc Vector4F[GradientBake.MaxStops];
        Span<float> offsets = stackalloc float[GradientBake.MaxStops];
        var count = GradientBake.PackStops(gradient, 1f, colors, offsets);

        return new LayerMask(
            type,
            geom0,
            geom1,
            new Vector4F(toBox.M11, toBox.M21, toBox.M31, 0f),
            new Vector4F(toBox.M12, toBox.M22, toBox.M32, 0f),
            new Vector4F(toBox.M13, toBox.M23, toBox.M33, 0f),
            new Vector4F(colors[0].W, colors[1].W, colors[2].W, colors[3].W),
            new Vector4F(colors[4].W, colors[5].W, colors[6].W, colors[7].W),
            new Vector4F(offsets[0], offsets[1], offsets[2], offsets[3]),
            new Vector4F(offsets[4], offsets[5], offsets[6], offsets[7]),
            new Vector4F(count, (float)gradient.SpreadMethod, 0f, 0f));
    }

    // From a window pixel (x, y, 1) to the mask's box, homogeneous: (u, v, q), the box point at (u / q, v / q). The box is
    // the element's own; an element of no size has its subtree's drawing instead.
    private bool MaskBox(IUIComponent layer, Rect2D fullScissor, out Matrix3x3F toBox)
    {
        toBox = default;
        var size = ApplySnap(layer).RenderSize;
        if (size.Width > 0 && size.Height > 0)
        {
            var world = World(layer);
            var scale = (float)_renderScale;
            var plane = new Matrix3x3F(
                world.M11 * scale, world.M12 * scale, world.M14,
                world.M21 * scale, world.M22 * scale, world.M24,
                world.M41 * scale, world.M42 * scale, world.M44);
            var unit = new Matrix3x3F(
                1f / (float)size.Width, 0f, 0f,
                0f, 1f / (float)size.Height, 0f,
                0f, 0f, 1f);
            toBox = Matrix3x3F.Invert(plane) * unit;
            return true;
        }

        var start = GroupIndexOf(layer);
        var region = LayerRegion(layer, fullScissor, start, start < 0 ? -1 : CloneSubtreeEnd(start));
        if (region.Extent.Width == 0 || region.Extent.Height == 0)
        {
            return false;
        }

        var width = (float)region.Extent.Width;
        var height = (float)region.Extent.Height;
        toBox = new Matrix3x3F(
            1f / width, 0f, 0f,
            0f, 1f / height, 0f,
            -region.Offset.X / width, -region.Offset.Y / height, 1f);
        return true;
    }

    private static bool WantsLayer(IUIComponent component) => component.IsLayer || component.OpacityMask != null;

    private void RecordLayerMark(RenderOpKind kind, IUIComponent layer, long order)
    {
        CloseLayer();
        RecordOp(new RenderOp { Kind = kind, Layer = layer, Order = order, OrderFirst = order });
        CloseLayer();
    }

    private Rect2D LayerRegion(IUIComponent layer, Rect2D fullScissor, int start, int end)
    {
        var clip = ResolveScissor(layer, World(layer), fullScissor, out _, out _);
        if (start < 0)
        {
            return clip;
        }

        var left = double.MaxValue;
        var top = double.MaxValue;
        var right = double.MinValue;
        var bottom = double.MinValue;
        for (var i = start; i < end; i++)
        {
            var group = _groups[i];
            if (group.Clones is { Count: > 0 })
            {
                return clip;
            }

            foreach (var unit in group.Units)
            {
                var ink = LocalInk(unit, ApplySnap(unit.Component).RenderSize).TransformToAABB(World(unit.Component));
                left = System.Math.Min(left, ink.X);
                top = System.Math.Min(top, ink.Y);
                right = System.Math.Max(right, ink.Right);
                bottom = System.Math.Max(bottom, ink.Bottom);
            }
        }

        if (left > right || top > bottom)
        {
            return new Rect2D { Offset = new Offset2D(), Extent = new Extent2D() };
        }

        var region = ToFramebufferScissor(new Rect(left, top, right - left, bottom - top), fullScissor);
        return Intersect(Grow(region, FringePixels), clip);
    }

    private int GroupIndexOf(IUIComponent component)
    {
        if (!_groupById.TryGetValue(component.RenderId, out var group) || !group.InOrder)
        {
            return -1;
        }

        var low = 0;
        var high = _groups.Count - 1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            var order = _groups[middle].Order;
            if (order == group.Order)
            {
                return ReferenceEquals(_groups[middle], group) ? middle : -1;
            }

            if (order < group.Order)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return -1;
    }

    private static Rect LocalInk(IRenderUnit unit, Size size)
    {
        var ink = new Rect(0, 0, size.Width, size.Height);
        var pen = 0.0;
        switch (unit)
        {
            case RectangleRenderUnit { Payload: { } rectangle }:
                ink = Union(ink, rectangle.DestinationRect);
                pen = rectangle.Pen?.Thickness ?? 0;
                break;
            case EllipseRenderUnit { Payload: { } ellipse }:
                ink = Union(ink, ellipse.DestinationRect);
                pen = (ellipse.Pen?.Thickness ?? 0) / 2;
                break;
            case RegularPolygonRenderUnit { Payload: { } polygon }:
                ink = Union(ink, polygon.DestinationRect);
                pen = MiterReach * (polygon.Pen?.Thickness ?? 0);
                break;
            case ImageRenderUnit { Payload: { } image }:
                ink = Union(ink, image.DestinationRect);
                break;
            case GeometryRenderUnit { Payload.Geometry: { } geometry } shape:
                var stroke = MiterReach * (shape.Payload.Pen?.Thickness ?? 0);
                ink = Union(ink, geometry.Bounds.Inflate(stroke).TransformToAABB(shape.Payload.LocalTransform));
                break;
            case LineRenderUnit { Payload: { } line }:
                ink = Union(ink, new Rect(
                    System.Math.Min(line.LineStart.X, line.LineEnd.X),
                    System.Math.Min(line.LineStart.Y, line.LineEnd.Y),
                    System.Math.Abs(line.LineEnd.X - line.LineStart.X),
                    System.Math.Abs(line.LineEnd.Y - line.LineStart.Y)));
                pen = MiterReach * (line.Pen?.Thickness ?? 0);
                break;
            case TextRenderUnit { Payload: { } text }:
                var overhang = text.DesiredSize.Height * TextOverhang;
                ink = Union(ink, new Rect(0, 0, text.DesiredSize.Width, text.DesiredSize.Height).Inflate(overhang)
                    .TransformToAABB(text.LocalTransform));
                break;
        }

        var reach = unit.RenderData == null
            ? 0
            : System.Math.Max(HaloRectCollector.MaxReach(unit.RenderData.Halo),
                HaloLivingCollector.MaxReach(unit.RenderData.LivingHalo));
        return ink.Inflate(pen + reach);
    }

    private static Rect2D Grow(Rect2D rect, int by) => new()
    {
        Offset = new Offset2D { X = rect.Offset.X - by, Y = rect.Offset.Y - by },
        Extent = new Extent2D { Width = rect.Extent.Width + (uint)(2 * by), Height = rect.Extent.Height + (uint)(2 * by) }
    };

    private bool OpensLayer(IUIComponent component)
    {
        if (!WantsLayer(component))
        {
            return false;
        }

        for (var c = component; c != null; c = ApplySnap(c).RenderParent)
        {
            if (c.RenderClones is { Count: > 0 })
            {
                return false;
            }
        }

        return true;
    }

    private float LayerAlpha(IUIComponent layer)
    {
        var snapshot = ApplySnap(layer);
        var alpha = snapshot.Opacity;
        for (var parent = snapshot.RenderParent; parent != null && !OpensLayer(parent); parent = ApplySnap(parent).RenderParent)
        {
            alpha *= ApplySnap(parent).Opacity;
        }

        return alpha;
    }
}
