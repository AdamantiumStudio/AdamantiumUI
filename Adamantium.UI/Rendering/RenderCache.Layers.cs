using System;
using System.Collections.Generic;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Fonts;
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
    private const ulong PrintSeed = 0xCBF29CE484222325UL;

    private readonly Stack<(int End, IUIComponent Layer)> _layerWalk = new();
    private OffscreenLayers _offscreenLayers;
    private long _framePaintEpoch;

    private bool ReplayOffscreenLayer(IGraphicsDevice device, IUIComponent layer, Rect2D fullScissor)
    {
        var start = GroupIndexOf(layer);
        return BeginOffscreenLayer(device, layer, fullScissor, start, start < 0 ? -1 : CloneSubtreeEnd(start), true);
    }

    private bool BeginOffscreenLayer(IGraphicsDevice device, IUIComponent layer, Rect2D fullScissor, int start, int end,
        bool allowReuse)
    {
        _offscreenLayers ??= new OffscreenLayers();
        var region = ScanLayer(layer, fullScissor, start, end, out var print, out var reusable);
        return _offscreenLayers.Begin(device, layer, region, print, reusable, allowReuse);
    }

    internal int LayersRedrawn => _offscreenLayers?.Redrawn ?? 0;

    internal int LayersReused => _offscreenLayers?.Reused ?? 0;

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
        var mirror = MirrorOf(layer);
        _offscreenLayers.End(device, alpha, scissor, mask, mirror,
            mirror.Shows ? MirrorScissor(layer, fullScissor) : scissor);
    }

    private (float Width, float Height, float Scale) BoxPixels(IUIComponent layer)
    {
        var size = ApplySnap(layer).RenderSize;
        var world = World(layer);
        var scale = (float)_renderScale;
        var across = MathF.Sqrt(world.M11 * world.M11 + world.M12 * world.M12) * scale;
        var down = MathF.Sqrt(world.M21 * world.M21 + world.M22 * world.M22) * scale;
        return ((float)size.Width * across, (float)size.Height * down, MathF.Min(across, down));
    }

    private LayerMirror MirrorOf(IUIComponent layer)
    {
        if (layer.Reflection is not { Shows: true } reflection)
        {
            return default;
        }

        var size = ApplySnap(layer).RenderSize;
        var (_, height, scale) = BoxPixels(layer);
        if (!(height > 0))
        {
            return default;
        }

        var world = World(layer);
        var bottom = float.MinValue;
        foreach (var (x, y) in (ReadOnlySpan<(float, float)>)[(0f, 0f), ((float)size.Width, 0f), (0f, (float)size.Height),
                     ((float)size.Width, (float)size.Height)])
        {
            var w = x * world.M14 + y * world.M24 + world.M44;
            if (MathF.Abs(w) > 1e-6f)
            {
                bottom = MathF.Max(bottom, (x * world.M12 + y * world.M22 + world.M42) / w * (float)_renderScale);
            }
        }

        return bottom == float.MinValue
            ? default
            : new LayerMirror(true, bottom, (float)reflection.Distance * scale,
                (float)Math.Min(reflection.Size, 1) * height, (float)Math.Clamp(reflection.Opacity, 0, 1));
    }

    private Rect2D MirrorScissor(IUIComponent layer, Rect2D fullScissor) =>
        ApplySnap(layer).RenderParent is { } parent
            ? ResolveScissor(parent, World(parent), fullScissor, out _, out _)
            : fullScissor;

    private LayerMask MaskOf(IUIComponent layer, Rect2D fullScissor, ref float alpha)
    {
        var brush = layer.OpacityMask?.Snapshot;
        var soft = default(Vector4F);
        var radii = default(Vector4F);
        if (layer.SoftEdge > 0)
        {
            var (width, height, scale) = BoxPixels(layer);
            if (width > 0 && height > 0)
            {
                soft = new Vector4F(width, height, (float)layer.SoftEdge * scale, 0f);
                radii = layer.ClipRadii * scale;
            }
        }

        if (brush != null)
        {
            alpha *= (float)brush.Opacity;
            if (brush is SolidColorBrush solid)
            {
                alpha *= solid.Color.ToVector4().W;
            }
        }

        var gradient = brush as GradientBrush;
        if ((gradient == null && soft.Z <= 0) || !MaskBox(layer, fullScissor, out var toBox))
        {
            return default;
        }

        var u = new Vector4F(toBox.M11, toBox.M21, toBox.M31, 0f);
        var v = new Vector4F(toBox.M12, toBox.M22, toBox.M32, 0f);
        var w = new Vector4F(toBox.M13, toBox.M23, toBox.M33, 0f);
        if (gradient == null)
        {
            return new LayerMask(0f, default, default, u, v, w, default, default, default, default, default, soft, radii);
        }

        var type = GradientBake.PackGeometry(gradient, out var geom0, out var geom1);
        Span<Vector4F> colors = stackalloc Vector4F[GradientBake.MaxStops];
        Span<float> offsets = stackalloc float[GradientBake.MaxStops];
        var count = GradientBake.PackStops(gradient, 1f, colors, offsets);

        return new LayerMask(
            type,
            geom0,
            geom1,
            u,
            v,
            w,
            new Vector4F(colors[0].W, colors[1].W, colors[2].W, colors[3].W),
            new Vector4F(colors[4].W, colors[5].W, colors[6].W, colors[7].W),
            new Vector4F(offsets[0], offsets[1], offsets[2], offsets[3]),
            new Vector4F(offsets[4], offsets[5], offsets[6], offsets[7]),
            new Vector4F(count, (float)gradient.SpreadMethod, 0f, 0f),
            soft,
            radii);
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

    private static bool WantsLayer(IUIComponent component) => component.IsLayer || component.OpacityMask != null ||
                                                              component.SoftEdge > 0 ||
                                                              component.Reflection is { Shows: true };

    private void RecordLayerMark(RenderOpKind kind, IUIComponent layer, long order)
    {
        CloseLayer();
        RecordOp(new RenderOp { Kind = kind, Layer = layer, Order = order, OrderFirst = order });
        CloseLayer();
    }

    private Rect2D LayerRegion(IUIComponent layer, Rect2D fullScissor, int start, int end) =>
        ScanLayer(layer, fullScissor, start, end, out _, out _);

    private Rect2D ScanLayer(IUIComponent layer, Rect2D fullScissor, int start, int end, out ulong print, out bool reusable)
    {
        print = PrintSeed;
        reusable = false;
        var layerWorld = World(layer);
        var clip = ResolveScissor(layer, layerWorld, fullScissor, out _, out _);
        if (start < 0)
        {
            return clip;
        }

        Mix(ref print, (float)_renderScale);
        MixMatrix(ref print, layerWorld);

        reusable = Core.Media.Brush.PaintEpoch == _framePaintEpoch && !MovedByTheCompositor(layer);
        var hasText = false;
        var left = double.MaxValue;
        var top = double.MaxValue;
        var right = double.MinValue;
        var bottom = double.MinValue;
        for (var i = start; i < end; i++)
        {
            var group = _groups[i];
            if (group.Clones is { Count: > 0 })
            {
                reusable = false;
                return clip;
            }

            if (i > start && reusable && OpensLayer(group.Component))
            {
                Mix(ref print, Identity(group.Component.OpacityMask?.Snapshot));
                MixMatrix(ref print, World(group.Component));
            }

            foreach (var unit in group.Units)
            {
                var world = World(unit.Component);
                var ink = LocalInk(unit, ApplySnap(unit.Component).RenderSize).TransformToAABB(world);
                left = System.Math.Min(left, ink.X);
                top = System.Math.Min(top, ink.Y);
                right = System.Math.Max(right, ink.Right);
                bottom = System.Math.Max(bottom, ink.Bottom);
                if (!reusable)
                {
                    continue;
                }

                reusable = MixContent(ref print, unit, ref hasText);
                MixMatrix(ref print, world);
                Mix(ref print, FadeWithin(unit.Component, layer));
                var scissor = ResolveScissor(unit.Component, world, fullScissor, out _, out var cull);
                Mix(ref print, cull ? 1 : 0);
                Mix(ref print, scissor.Offset.X);
                Mix(ref print, scissor.Offset.Y);
                Mix(ref print, scissor.Extent.Width);
                Mix(ref print, scissor.Extent.Height);
                MixRoundedClip(ref print, unit.Component, fullScissor);
            }

            if (i > start && MirrorOf(group.Component) is { Shows: true } mirror)
            {
                bottom = System.Math.Max(bottom, (mirror.Bottom + mirror.Gap + mirror.Length) / _renderScale);
            }
        }

        if (left > right || top > bottom)
        {
            reusable = false;
            return new Rect2D { Offset = new Offset2D(), Extent = new Extent2D() };
        }

        if (hasText)
        {
            reusable &= !FontAtlasStore.HasPendingGlyphs;
            Mix(ref print, FontAtlasStore.LandedVersion);
        }

        var region = Intersect(Grow(ToFramebufferScissor(new Rect(left, top, right - left, bottom - top), fullScissor),
            FringePixels), clip);
        Mix(ref print, region.Offset.X);
        Mix(ref print, region.Offset.Y);
        Mix(ref print, region.Extent.Width);
        Mix(ref print, region.Extent.Height);
        return region;
    }

    private bool MovedByTheCompositor(IUIComponent layer)
    {
        foreach (var owner in _compositedOwners)
        {
            if (ReferenceEquals(owner, layer) || IsVisualDescendantOf(owner, layer) || IsVisualDescendantOf(layer, owner))
            {
                return true;
            }
        }

        return false;
    }

    private void MixRoundedClip(ref ulong print, IUIComponent component, Rect2D fullScissor)
    {
        for (var owner = component; owner != null; owner = ApplySnap(owner).RenderParent)
        {
            var s = ApplySnap(owner);
            if (!s.ClipToBounds || s.ClipRadii == Vector4F.Zero)
            {
                continue;
            }

            var box = ToFramebufferScissor(new Rect(0, 0, s.RenderSize.Width, s.RenderSize.Height)
                .TransformToAABB(World(owner)), fullScissor);
            Mix(ref print, box.Offset.X);
            Mix(ref print, box.Offset.Y);
            Mix(ref print, box.Extent.Width);
            Mix(ref print, box.Extent.Height);
            Mix(ref print, s.ClipRadii.X);
            Mix(ref print, s.ClipRadii.Y);
            Mix(ref print, s.ClipRadii.Z);
            Mix(ref print, s.ClipRadii.W);
            return;
        }

        Mix(ref print, 0);
    }

    private float FadeWithin(IUIComponent component, IUIComponent layer)
    {
        var snapshot = ApplySnap(component);
        var fade = snapshot.SelfOpacity;
        for (var c = component; c != null && !ReferenceEquals(c, layer); c = ApplySnap(c).RenderParent)
        {
            fade *= ApplySnap(c).Opacity;
        }

        return fade;
    }

    private static bool MixContent(ref ulong print, IRenderUnit unit, ref bool hasText)
    {
        if (unit.RenderData is { } data)
        {
            if (data.LivingHalo != null)
            {
                return false;
            }

            Mix(ref print, Identity(data.Halo));
        }

        Mix(ref print, Identity(unit));
        switch (unit)
        {
            case RectangleRenderUnit { Payload: { } rectangle }:
                Mix(ref print, Identity(rectangle));
                return Still(ref print, rectangle.Brush) & Still(ref print, rectangle.BorderBrush)
                       & Still(ref print, rectangle.Pen);
            case EllipseRenderUnit { Payload: { } ellipse }:
                Mix(ref print, Identity(ellipse));
                return Still(ref print, ellipse.Brush) & Still(ref print, ellipse.Pen);
            case RegularPolygonRenderUnit { Payload: { } polygon }:
                Mix(ref print, Identity(polygon));
                return Still(ref print, polygon.Brush) & Still(ref print, polygon.Pen);
            case GeometryRenderUnit { Payload: { } shape }:
                Mix(ref print, Identity(shape));
                return Still(ref print, shape.Brush) & Still(ref print, shape.Pen);
            case LineRenderUnit { Payload: { } line }:
                Mix(ref print, Identity(line));
                return Still(ref print, line.Pen);
            case TextRenderUnit { Payload: { } text }:
                hasText = true;
                Mix(ref print, Identity(text));
                Mix(ref print, text.Revision);
                return Still(ref print, text.Foreground) & Still(ref print, text.Background)
                       & Still(ref print, text.Stroke);
            default:
                return false;
        }
    }

    private static bool Still(ref ulong print, Brush brush)
    {
        Mix(ref print, Identity(brush));
        return brush is null or SolidColorBrush or GradientBrush;
    }

    private static bool Still(ref ulong print, Pen pen)
    {
        if (pen == null)
        {
            Mix(ref print, 0);
            return true;
        }

        Mix(ref print, Identity(pen));
        return Still(ref print, pen.Brush);
    }

    private static long Identity(object value) => ObjectIds.Of(value);

    private static void Mix(ref ulong print, long value)
    {
        print ^= (ulong)value;
        print *= 0x100000001B3UL;
        print ^= print >> 29;
    }

    private static void Mix(ref ulong print, float value) => Mix(ref print, BitConverter.SingleToInt32Bits(value));

    private static void MixMatrix(ref ulong print, Matrix4x4F m)
    {
        for (var i = 0; i < 16; i++)
        {
            Mix(ref print, m[i]);
        }
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
