using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Fill;
using Adamantium.UI.FX;
using Adamantium.Vulkan.Core;
using Buffer = Adamantium.Graphics.Buffer;

namespace Adamantium.UI.Rendering.RenderUnits;

/// <summary>
/// The per-unit analytic AA fringe: a coverage ring per closed contour (FringeGeometry) drawn over the solid body, for
/// fills outside the instanced path. Built once; the shader sizes it in device pixels.
/// </summary>
public sealed class GpuFillRenderComponent : UIRenderComponent
{
    // Fringe width in DEVICE pixels, applied by the vertex shader.
    private const float DeviceFringePx = 1.0f;
    private const MemoryPropertyFlags FillMemory = MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.DeviceLocal;

    private readonly IGraphicsDevice _device;
    private readonly FillFringeEffect _effect;
    private readonly GradientUniforms _gradient;
    private readonly ReusableBuffer _ringBuffer;
    private RingEdge[] _ring;
    private Vector4F _localBounds;   // shape local bounds (minX, minY, sizeX, sizeY): the fragment->gradient uv basis

    private ulong RingBytes => (ulong)(_ring.Length * Marshal.SizeOf<RingEdge>());

    // The fill brush, read LIVE at Render (like the body's GeometryRenderComponent.Background) so an in-place color
    // change or a cheap brush repoint shows without rebuilding the contour. A non-solid brush => the fringe doesn't draw
    // (the CPU body doesn't render non-solid fills either), so no stale outline is left when a fill turns into a gradient.
    public Brush Brush { get; set; }

    /// <summary>The box a gradient spans when it is not the contour's own bounds - the body's, so the edge takes the
    /// body's colors.</summary>
    public Rect? GradientBox { get; set; }

    public GpuFillRenderComponent(IGraphicsDevice device, UIBasicEffect uiBasicEffect, FillFringeEffect effect,
        IReadOnlyList<(Vector2<double>[] Points, bool IsClosed)> contours, Brush brush, GpuBufferManager bufferManager,
        Rect? cut = null) : base(device, uiBasicEffect, null, bufferManager)
    {
        _device = device;
        _effect = effect;
        _gradient = new GradientUniforms(effect);
        Brush = brush;

        _ring = FringeGeometry.BuildRing(FringeGeometry.Build(contours), cut);
        _localBounds = GradientUniforms.BoundsOf(contours);

        // Rented from the buffer manager (ReusableBuffer), not allocated per frame: a same-size geometry change (a
        // resize) rewrites the ring into the existing slot with no Vulkan allocation.
        _ringBuffer = ToDispose(BufferManager.CreateBuffer(
            BufferUsageFlags.StorageBuffer | BufferUsageFlags.ShaderDeviceAddress, FillMemory, "fill outline"));
        _ringBuffer.Reserve(RingBytes);
        _ringBuffer.Invalidate();
    }

    // Same-topology update (a resize): a re-tessellation that yields a ring of the SAME size rewrites it into the
    // existing slot - no new component, no allocation. Returns false when the size differs (a contour added/removed, or
    // a CornerRadius change altered an arc's segment count), so the caller rebuilds the component.
    public bool TryUpdateContours(IReadOnlyList<(Vector2<double>[] Points, bool IsClosed)> contours, Brush brush,
        Rect? cut = null)
    {
        var ring = FringeGeometry.BuildRing(FringeGeometry.Build(contours), cut);
        if (ring.Length != _ring.Length) return false;

        _ring = ring;
        _ringBuffer.Invalidate();   // re-upload the ring to the current slot
        Brush = brush;
        _localBounds = GradientUniforms.BoundsOf(contours);
        return true;
    }

    // Upload runs out of the render pass (beforeRenderPass hook) and only when this frame's slot is stale - a static
    // fill settles to zero work. The ring itself carries no scale, so a zoom or a transform change never invalidates
    // it; only a contour change does.
    public override void PreRender()
    {
        if (!AnalyticAa.Enabled || _ring.Length == 0) return;
        if (Brush is not (SolidColorBrush or GradientBrush or PatternBrush or NoiseBrush)) return;

        var vertices = _ringBuffer.Acquire(RingBytes, out var write);
        if (write) vertices.SetData(_ring, 0, (uint)_ring.Length);
    }

    public override void Render()
    {
        if (!AnalyticAa.Enabled || _ring.Length == 0 || Brush is not (SolidColorBrush or GradientBrush or PatternBrush or NoiseBrush)) return;

        _effect.Projection.SetValue(RenderData.TransformMatrix * RenderData.ProjectionMatrix);
        // The VS offsets the ring in DEVICE pixels, so it needs the render target's pixel size (the viewport already
        // carries the designer zoom: it is sized ClientSize x RenderScale while the projection stays logical).
        var vp = _device.CurrentViewports;
        if (vp is { Length: > 0 }) _effect.ViewportSize.SetValue(new Vector2F(vp[0].Width, vp[0].Height));
        _effect.FringePixels.SetValue(DeviceFringePx);
        if (Brush is GradientBrush g)
        {
            // The same gradient as the fill (packed by the shared GradientBake, same as the fill batch), so the AA edge
            // matches the fill instead of one flat color.
            var bounds = GradientBox is { } box
                ? new Vector4F((float)box.X, (float)box.Y, (float)box.Width, (float)box.Height)
                : _localBounds;
            _gradient.Set(g, (float)(g.Opacity * RenderData.Opacity), bounds);
        }
        else if (Brush is SolidColorBrush solid)
        {
            var color = solid.Color.ToVector4();
            color.W *= (float)solid.Opacity * RenderData.Opacity;   // color alpha x brush Opacity x element Opacity
            _effect.FillColor.SetValue(color);
            _gradient.Clear();
        }
        else   // PatternBrush / NoiseBrush: a flat representative edge color (the 1px ring doesn't evaluate the pattern)
        {
            _effect.FillColor.SetValue(PatternFringeColor(Brush));
            _gradient.Clear();
        }

        _device.VertexType = null;   // the ring is read by address, six corners per edge
        _device.PolygonMode = PolygonMode.Fill;
        _device.PrimitiveTopology = PrimitiveTopology.TriangleList;
        _device.ColorBlendEquation = ColorBlendEquations.AlphaBlend;
        _device.DepthCompareFunction = CompareOp.Always;
        _device.DepthTestEnabled = true;
        _device.DepthWriteEnable = true;
        var ring = _ringBuffer.Acquire(RingBytes, out var write);
        if (write) ring.SetData(_ring, 0, (uint)_ring.Length);
        _effect.RingEdges.SetValue(ring.GetDeviceAddress());
        _effect.FillFringeDrawPass.Apply();

        _device.Draw((uint)_ring.Length * 6, 1);
    }


    // Analytic-AA fringe color for a procedural pattern/noise fill: the ring is 1px, so rather than evaluate the whole
    // pattern there (a heavy fringe shader), color it with the brush's LOW color (Color1). A procedural field is mostly its
    // background/low value, so a shape edge is dominated by Color1 - the ring blends into it instead of ringing a bright
    // midpoint. Not per-fragment exact, but smooths the edge without a highlighted rim. Alpha folds brush + element opacity.
    private Vector4F PatternFringeColor(Brush brush)
    {
        Color c1;
        double bo;
        if (brush is PatternBrush pb) { c1 = pb.Color1; bo = pb.Opacity; }
        else { var nb = (NoiseBrush)brush; c1 = nb.Color1; bo = nb.Opacity; }
        var v = c1.ToVector4();
        v.W *= (float)bo * RenderData.Opacity;
        return v;
    }
}
