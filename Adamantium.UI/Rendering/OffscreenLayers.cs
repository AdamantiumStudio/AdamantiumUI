using System;
using System.Collections.Generic;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.FX;
using Adamantium.Vulkan.Core;

namespace Adamantium.UI.Rendering;

internal sealed class OffscreenLayers : IDisposable
{
    private const long IdleFrames = 120;

    private readonly List<OffscreenLayerLevel> _levels = [];
    private readonly Stack<OpenOffscreenLayer> _open = new();
    private readonly Dictionary<object, CachedLayer> _cached = new();
    private readonly List<object> _idle = [];
    private LayerCompositeEffect _composite;
    private LayerSoftEdgeEffect _soft;
    private long _frame;

    /// <summary>How many layers this frame drew their subtree into their texture.</summary>
    public int Redrawn { get; private set; }

    /// <summary>How many layers this frame laid down the texture they already held.</summary>
    public int Reused { get; private set; }

    internal uint TallestLayerTexture
    {
        get
        {
            var tallest = 0u;
            foreach (var entry in _cached.Values)
            {
                tallest = Math.Max(tallest, entry.TextureHeight);
            }

            return tallest;
        }
    }

    internal int LayersHoldingBands
    {
        get
        {
            var count = 0;
            foreach (var entry in _cached.Values)
            {
                if (entry.BandsRing != null)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>Starts a frame: counts from zero and gives up the textures of layers not drawn for a while.</summary>
    public void BeginFrame(IGraphicsDevice device)
    {
        _frame++;
        Redrawn = 0;
        Reused = 0;
        if (device is not GraphicsDevice gd)
        {
            return;
        }

        foreach (var (owner, entry) in _cached)
        {
            if (_frame - entry.LastUsed > IdleFrames)
            {
                _idle.Add(owner);
            }
        }

        foreach (var owner in _idle)
        {
            var idle = _cached[owner];
            Release(gd, idle.Ring);
            if (idle.BandsRing != null)
            {
                Release(gd, idle.BandsRing);
            }

            if (idle.SoftRing != null)
            {
                Release(gd, idle.SoftRing);
                Release(gd, new[] { idle.SoftScratch });
            }

            _cached.Remove(owner);
        }

        _idle.Clear();
    }

    /// <summary>The space drawing lands in, inside an open layer; outside one it is the caller's own.</summary>
    public DrawSpace Space { get; private set; }

    /// <summary>Whether drawing lands in a layer's own space - its own, or one it is drawn inside of.</summary>
    public bool InOwnSpace => _spaces.Count > 0 && Space.Own;

    /// <summary>The space the innermost open layer was opened in, which its close goes back to, and whether it was opened
    /// drawn whole.</summary>
    public (DrawSpace Outer, bool Whole) Closing => (_spaces.Peek().Outer, _spaces.Peek().Whole);

    // One for every open layer and bands, whatever became of them: what to go back to, decided when they opened.
    private readonly Stack<(DrawSpace Outer, Viewport Viewport, bool Whole)> _spaces = new();

    // Into the space drawing lands in from here: the one it was in - the given projection and bounds - or, drawn whole,
    // the region's own: its viewport and a projection onto it, so a window pixel lands on the same surface pixel and what
    // lies past the window is drawn too.
    private DrawSpace EnterSpace(GraphicsDevice gd, Rect2D region, Matrix4x4F projection, Rect2D bounds, bool whole)
    {
        var outer = new DrawSpace(projection, bounds, _spaces.Count > 0 && Space.Own);
        var viewport = gd is { CurrentViewports.Length: > 0 } ? gd.CurrentViewports[0] : default;
        _spaces.Push((outer, viewport, whole));
        if (!whole)
        {
            return Space = outer;
        }

        if (gd == null || region.Extent.Width == 0 || region.Extent.Height == 0 || !(viewport.Width > 0)
            || !(viewport.Height > 0))
        {
            return Space = outer with { Bounds = region, Own = true };
        }

        var width = (float)region.Extent.Width;
        var height = (float)region.Extent.Height;
        var onto = Matrix4x4F.Identity;
        onto.M11 = viewport.Width / width;
        onto.M22 = viewport.Height / height;
        onto.M41 = onto.M11 - 1f + 2f * (viewport.X - region.Offset.X) / width;
        onto.M42 = onto.M22 - 1f + 2f * (viewport.Y - region.Offset.Y) / height;
        gd.SetViewports(new Viewport
        {
            X = region.Offset.X, Y = region.Offset.Y, Width = width, Height = height,
            MinDepth = viewport.MinDepth, MaxDepth = viewport.MaxDepth
        });
        gd.SetScissors(region);
        return Space = new DrawSpace(outer.Projection * onto, region, true);
    }

    private void LeaveSpace(GraphicsDevice gd)
    {
        var (outer, viewport, whole) = _spaces.Pop();
        if (whole && gd != null && viewport.Width > 0)
        {
            gd.SetViewports(viewport);
            gd.SetScissors(outer.Bounds);
        }

        Space = outer;
    }

    /// <summary>Opens the texture <paramref name="owner"/>'s own bands - its glow, its shadow - are drawn into, over the
    /// <paramref name="region"/> its layer covers: they are laid down under the layer, with it, apart from what its soft
    /// edge fades. Drawn <paramref name="whole"/>, in the region's own space; otherwise in the one opened over, its
    /// <paramref name="projection"/> and <paramref name="bounds"/>. <see cref="EndBands"/> goes back to the target and the
    /// space it was opened over.</summary>
    public DrawSpace BeginBands(IGraphicsDevice device, object owner, Rect2D region, Matrix4x4F projection, Rect2D bounds,
        bool whole)
    {
        if (device is not GraphicsDevice gd || gd.CurrentRenderTarget == null
            || region.Extent.Width == 0 || region.Extent.Height == 0)
        {
            _bands.Push(default);
            return EnterSpace(device as GraphicsDevice, region, projection, bounds, whole);
        }

        if (!_cached.TryGetValue(owner, out var entry))
        {
            entry = new CachedLayer();
            _cached[owner] = entry;
        }

        entry.LastUsed = _frame;
        EnsureRing(gd, entry, region.Extent.Width, region.Extent.Height);
        EnsureBands(gd, entry);
        var slot = (entry.BandsLatest + 1) % entry.BandsRing.Length;
        var target = entry.BandsRing[slot];
        _bands.Push(new OpenBands(gd.CurrentRenderTarget, gd.CurrentDepthStencilBuffer, gd.TargetOrigin, gd.ClearColor,
            gd.MSAALevel, target, entry, slot));

        gd.SuspendRendering();
        gd.InsertImageMemoryBarrier(gd.CurrentCommandBuffer, target,
            AccessFlagBits2.ShaderReadBit, AccessFlagBits2.ColorAttachmentWriteBit,
            ImageLayout.Undefined, ImageLayout.ColorAttachmentOptimal,
            PipelineStageFlagBits2.FragmentShaderBit, PipelineStageFlagBits2.ColorAttachmentOutputBit);
        gd.SetRenderTargets(target);
        gd.SetDepthBuffer(null);
        gd.ClearColor = Colors.Transparent;
        gd.MSAALevel = MSAALevel.None;
        gd.BeginRendering(gd.CurrentCommandBuffer);
        gd.TargetOrigin = new NativePoint(region.Offset.X, region.Offset.Y);
        return EnterSpace(gd, region, projection, bounds, whole);
    }

    /// <summary>Closes the bands <see cref="BeginBands"/> opened, keeping them for the layer laid down this frame, and
    /// gives back the space they were opened in.</summary>
    public DrawSpace EndBands(IGraphicsDevice device)
    {
        var gd = device as GraphicsDevice;
        var open = _bands.Pop();
        LeaveSpace(gd);
        if (gd == null || open.Texture == null)
        {
            return Space;
        }

        gd.SuspendRendering();
        gd.InsertImageMemoryBarrier(gd.CurrentCommandBuffer, open.Texture,
            AccessFlagBits2.ColorAttachmentWriteBit, AccessFlagBits2.ShaderReadBit,
            ImageLayout.ColorAttachmentOptimal, ImageLayout.ShaderReadOnlyOptimal,
            PipelineStageFlagBits2.ColorAttachmentOutputBit, PipelineStageFlagBits2.FragmentShaderBit);
        gd.SetRenderTargets(open.Target);
        gd.SetDepthBuffer(open.Depth);
        gd.ClearColor = open.Clear;
        gd.MSAALevel = open.Samples;
        gd.TargetOrigin = open.Origin;
        gd.ResumeRendering();

        open.Entry.BandsLatest = open.Slot;
        open.Entry.BandsFrame = _frame;
        return Space;
    }

    private readonly record struct OpenBands(IRenderTarget Target, IDepthStencilBuffer Depth, NativePoint Origin,
        Color Clear, MSAALevel Samples, IRenderTarget Texture, CachedLayer Entry, int Slot);

    private readonly Stack<OpenBands> _bands = new();

    private static void EnsureBands(GraphicsDevice gd, CachedLayer entry)
    {
        if (entry.BandsRing != null && entry.BandsRing.Length == entry.Ring.Length
            && entry.BandsWidth == entry.TextureWidth && entry.BandsHeight == entry.TextureHeight)
        {
            return;
        }

        if (entry.BandsRing != null)
        {
            Release(gd, entry.BandsRing);
        }

        var format = entry.Ring[0].SurfaceFormat;
        entry.BandsWidth = entry.TextureWidth;
        entry.BandsHeight = entry.TextureHeight;
        entry.BandsRing = new IRenderTarget[entry.Ring.Length];
        for (var i = 0; i < entry.BandsRing.Length; i++)
        {
            entry.BandsRing[i] = gd.CreateRenderTarget(entry.TextureWidth, entry.TextureHeight, MSAALevel.None, format,
                ImageUsageFlagBits.SampledBit, ImageLayout.ShaderReadOnlyOptimal, $"LayerBands:{i}");
        }

        entry.BandsLatest = -1;
        entry.BandsFrame = -1;
    }

    /// <summary>Opens <paramref name="owner"/>'s layer over <paramref name="region"/> (window device pixels) and gives the
    /// space drawing lands in: drawn <paramref name="whole"/>, the region's own; otherwise the one opened over, its
    /// <paramref name="projection"/> and <paramref name="bounds"/>. <paramref name="reused"/> means its
    /// texture already holds a drawing with this <paramref name="print"/>: draw nothing until <see cref="End"/>, which
    /// lays it down. Otherwise drawing lands in a fresh texture, kept for reuse when <paramref name="reusable"/>.</summary>
    public DrawSpace Begin(IGraphicsDevice device, object owner, Rect2D region, ulong print, bool reusable,
        bool allowReuse, Matrix4x4F projection, Rect2D bounds, bool whole, out bool reused)
    {
        reused = false;
        if (device is not GraphicsDevice gd || gd.CurrentRenderTarget == null
            || region.Extent.Width == 0 || region.Extent.Height == 0)
        {
            _open.Push(default);
            return EnterSpace(device as GraphicsDevice, region, projection, bounds, whole);
        }

        if (!_cached.TryGetValue(owner, out var entry))
        {
            entry = new CachedLayer();
            _cached[owner] = entry;
        }

        entry.LastUsed = _frame;
        if (allowReuse && reusable && entry.Valid && entry.Print == print
            && entry.Width == region.Extent.Width && entry.Height == region.Extent.Height)
        {
            _open.Push(new OpenOffscreenLayer(null, null, default, default, default, entry.Ring[entry.Latest],
                entry.TextureWidth, entry.TextureHeight, region, entry, entry.Latest, print, true, true));
            Reused++;
            reused = true;
            return EnterSpace(gd, region, projection, bounds, whole);
        }

        EnsureRing(gd, entry, region.Extent.Width, region.Extent.Height);
        var slot = (entry.Latest + 1) % entry.Ring.Length;
        var color = entry.Ring[slot];
        var level = LevelAt(gd, _open.Count, entry.TextureWidth, entry.TextureHeight);
        var depth = level.Depth.Length > 0 ? level.Depth[(int)(gd.CurrentFrame % (uint)level.Depth.Length)] : null;

        _open.Push(new OpenOffscreenLayer(gd.CurrentRenderTarget, gd.CurrentDepthStencilBuffer, gd.TargetOrigin, gd.ClearColor,
            gd.MSAALevel, color, entry.TextureWidth, entry.TextureHeight, region, entry, slot, print, reusable, false));

        gd.SuspendRendering();
        var commandBuffer = gd.CurrentCommandBuffer;
        gd.InsertImageMemoryBarrier(commandBuffer, color,
            AccessFlagBits2.ShaderReadBit, AccessFlagBits2.ColorAttachmentWriteBit,
            ImageLayout.Undefined, ImageLayout.ColorAttachmentOptimal,
            PipelineStageFlagBits2.FragmentShaderBit, PipelineStageFlagBits2.ColorAttachmentOutputBit);
        if (depth != null)
        {
            gd.InsertImageMemoryBarrier(commandBuffer, depth,
                AccessFlagBits2.DepthStencilAttachmentWriteBit, AccessFlagBits2.DepthStencilAttachmentWriteBit,
                ImageLayout.Undefined, ImageLayout.DepthStencilAttachmentOptimal,
                PipelineStageFlagBits2.EarlyFragmentTestsBit | PipelineStageFlagBits2.LateFragmentTestsBit,
                PipelineStageFlagBits2.EarlyFragmentTestsBit | PipelineStageFlagBits2.LateFragmentTestsBit);
        }

        gd.SetRenderTargets(color);
        gd.SetDepthBuffer(depth);
        gd.ClearColor = Colors.Transparent;
        gd.MSAALevel = MSAALevel.None;
        gd.BeginRendering(commandBuffer);
        gd.TargetOrigin = new NativePoint(region.Offset.X, region.Offset.Y);
        Redrawn++;
        return EnterSpace(gd, region, projection, bounds, whole);
    }

    /// <summary>Closes the innermost layer, goes back to the target it was opened over and lays the layer down there with
    /// <paramref name="alpha"/> and <paramref name="mask"/>, faded <paramref name="soft"/> device pixels in from what it
    /// drew, cut by <paramref name="scissor"/> (device pixels of the window); and its reflection, cut by
    /// <paramref name="mirrorScissor"/>, where <paramref name="mirror"/> shows. <paramref name="softCut"/> marks the sides
    /// (left, top, right, bottom) where its ancestors cut what it drew short: the drawing goes on past them, so nothing
    /// fades there. Drawing goes back to the space the layer was opened in (<see cref="Closing"/>).</summary>
    public void End(IGraphicsDevice device, float alpha, Rect2D scissor, in LayerMask mask, float soft, Vector4F softCut,
        in LayerMirror mirror, Rect2D mirrorScissor, Vector4F clipBox, Vector4F clipRadii)
    {
        var gd = device as GraphicsDevice;
        var open = _open.Pop();
        if (gd == null || open.Color == null)
        {
            LeaveSpace(gd);
            return;
        }

        var entry = open.Entry;
        var blur = soft > 0f && (!open.Reused || entry.Soft != soft || entry.SoftRing == null);
        if (!open.Reused || blur)
        {
            // Back to the target the layer was opened over: a reused layer never left it, so it is the current one.
            var target = open.Reused ? gd.CurrentRenderTarget : open.Target;
            var depth = open.Reused ? gd.CurrentDepthStencilBuffer : open.Depth;
            var clear = open.Reused ? gd.ClearColor : open.Clear;
            var samples = open.Reused ? gd.MSAALevel : open.Samples;
            var origin = open.Reused ? gd.TargetOrigin : open.Origin;

            gd.SuspendRendering();
            if (!open.Reused)
            {
                gd.InsertImageMemoryBarrier(gd.CurrentCommandBuffer, open.Color,
                    AccessFlagBits2.ColorAttachmentWriteBit, AccessFlagBits2.ShaderReadBit,
                    ImageLayout.ColorAttachmentOptimal, ImageLayout.ShaderReadOnlyOptimal,
                    PipelineStageFlagBits2.ColorAttachmentOutputBit, PipelineStageFlagBits2.FragmentShaderBit);
            }

            if (blur)
            {
                BlurCoverage(gd, open, soft, softCut);
            }

            gd.SetRenderTargets(target);
            gd.SetDepthBuffer(depth);
            gd.ClearColor = clear;
            gd.MSAALevel = samples;
            gd.TargetOrigin = origin;
            gd.ResumeRendering();
        }

        if (!open.Reused)
        {
            entry.Latest = open.Slot;
            entry.Print = open.Print;
            entry.Valid = open.Reusable;
            entry.Width = open.Region.Extent.Width;
            entry.Height = open.Region.Extent.Height;
            if (!blur)
            {
                entry.Soft = 0f;
            }
        }

        if (soft <= 0f && entry.SoftRing != null)
        {
            ReleaseSoft(gd, entry);
        }

        if (soft <= 0f && entry.BandsRing != null)
        {
            Release(gd, entry.BandsRing);
            entry.BandsRing = null;
            entry.BandsLatest = -1;
            entry.BandsFrame = -1;
        }

        // Laid down in the space it was opened in.
        LeaveSpace(gd);
        var softTexture = soft > 0f ? entry.SoftRing?[open.Slot] : null;
        var bands = entry.BandsFrame == _frame && entry.BandsLatest >= 0 ? entry.BandsRing[entry.BandsLatest] : null;
        if (alpha > 0f)
        {
            if (mirror.Shows)
            {
                Composite(gd, open, alpha, mirrorScissor, mask, softTexture, bands, mirror, 2f, clipBox, clipRadii);
            }

            Composite(gd, open, alpha, scissor, mask, softTexture, bands, mirror, mirror.Shows ? 1f : 0f, clipBox,
                clipRadii);
        }
    }

    // How deep inside what the layer drew each texel lies: its coverage blurred across, then down, into the slot's own
    // texture beside the color, so a reused layer keeps it.
    private void BlurCoverage(GraphicsDevice gd, OpenOffscreenLayer open, float soft, Vector4F cut)
    {
        var entry = open.Entry;
        EnsureSoft(gd, entry);
        _soft ??= new LayerSoftEdgeEffect(gd);

        var viewport = gd.CurrentViewports.Length > 0 ? gd.CurrentViewports[0] : default;
        var scissor = gd.CurrentScissors.Length > 0 ? gd.CurrentScissors[0] : default;
        var origin = gd.TargetOrigin;
        var blend = gd.ColorBlendEnabled;
        var region = open.Region;
        var area = new Rect2D { Offset = new Offset2D(), Extent = region.Extent };
        var texelX = 1f / open.TextureWidth;
        var texelY = 1f / open.TextureHeight;
        var limit = new Vector4F(region.Extent.Width * texelX, region.Extent.Height * texelY, texelX, texelY);
        var sigma = soft * 0.5f;

        BlurPass(gd, open.Color, entry.SoftScratch, area, new Vector4F(texelX, 0f, sigma, 0f), limit, cut);
        BlurPass(gd, entry.SoftScratch, entry.SoftRing[open.Slot], area, new Vector4F(0f, texelY, sigma, 1f), limit, cut);
        entry.Soft = soft;

        gd.TargetOrigin = origin;
        gd.SetViewports(viewport);
        gd.SetScissors(scissor);
        gd.ColorBlendEnabled = blend;
    }

    private void BlurPass(GraphicsDevice gd, IRenderTarget source, IRenderTarget target, Rect2D area, Vector4F axis,
        Vector4F limit, Vector4F cut)
    {
        var commandBuffer = gd.CurrentCommandBuffer;
        gd.InsertImageMemoryBarrier(commandBuffer, target,
            AccessFlagBits2.ShaderReadBit, AccessFlagBits2.ColorAttachmentWriteBit,
            ImageLayout.Undefined, ImageLayout.ColorAttachmentOptimal,
            PipelineStageFlagBits2.FragmentShaderBit, PipelineStageFlagBits2.ColorAttachmentOutputBit);
        gd.SetRenderTargets(target);
        gd.SetDepthBuffer(null);
        gd.ClearColor = Colors.Transparent;
        gd.MSAALevel = MSAALevel.None;
        gd.BeginRendering(commandBuffer);
        gd.TargetOrigin = default;
        gd.SetViewports(new Viewport { Width = area.Extent.Width, Height = area.Extent.Height, MaxDepth = 1.0f });
        gd.SetScissors(area);
        gd.ColorBlendEnabled = false;
        gd.DepthTestEnabled = false;
        gd.DepthWriteEnable = false;
        gd.PrimitiveTopology = PrimitiveTopology.TriangleList;
        gd.VertexType = null;

        _soft.BlurAxis.SetValue(axis);
        _soft.BlurLimit.SetValue(limit);
        _soft.BlurCut.SetValue(cut);
        _soft.SourceTexture.SetResource(source);
        _soft.SourceSampler.SetResource(SamplerStates.LinearClampToEdge);
        _soft.LayerSoftEdgeBlurPass.Apply();
        gd.Draw(3, 1);
        gd.SuspendRendering();

        gd.InsertImageMemoryBarrier(commandBuffer, target,
            AccessFlagBits2.ColorAttachmentWriteBit, AccessFlagBits2.ShaderReadBit,
            ImageLayout.ColorAttachmentOptimal, ImageLayout.ShaderReadOnlyOptimal,
            PipelineStageFlagBits2.ColorAttachmentOutputBit, PipelineStageFlagBits2.FragmentShaderBit);
    }

    private static void EnsureSoft(GraphicsDevice gd, CachedLayer entry)
    {
        if (entry.SoftRing != null && entry.SoftRing.Length == entry.Ring.Length && entry.SoftWidth == entry.TextureWidth
            && entry.SoftHeight == entry.TextureHeight)
        {
            return;
        }

        if (entry.SoftRing != null)
        {
            ReleaseSoft(gd, entry);
        }

        var format = entry.Ring[0].SurfaceFormat;
        entry.SoftWidth = entry.TextureWidth;
        entry.SoftHeight = entry.TextureHeight;
        entry.SoftRing = new IRenderTarget[entry.Ring.Length];
        for (var i = 0; i < entry.SoftRing.Length; i++)
        {
            entry.SoftRing[i] = SoftTarget(gd, entry, format, $"LayerSoft:{i}");
        }

        entry.SoftScratch = SoftTarget(gd, entry, format, "LayerSoftScratch");
        entry.Soft = 0f;
    }

    private static void ReleaseSoft(GraphicsDevice gd, CachedLayer entry)
    {
        Release(gd, entry.SoftRing);
        Release(gd, new[] { entry.SoftScratch });
        entry.SoftRing = null;
        entry.SoftScratch = null;
        entry.Soft = 0f;
    }

    private static IRenderTarget SoftTarget(GraphicsDevice gd, CachedLayer entry, Adamantium.Imaging.SurfaceFormat format,
        string name) =>
        gd.CreateRenderTarget(entry.TextureWidth, entry.TextureHeight, MSAALevel.None, format,
            ImageUsageFlagBits.SampledBit, ImageLayout.ShaderReadOnlyOptimal, name);

    private void Composite(GraphicsDevice gd, OpenOffscreenLayer open, float alpha, Rect2D scissor, in LayerMask mask,
        IRenderTarget soft, IRenderTarget bands, in LayerMirror mirror, float role, Vector4F clipBox, Vector4F clipRadii)
    {
        if (gd.CurrentViewports.Length == 0)
        {
            return;
        }

        _composite ??= new LayerCompositeEffect(gd);
        var viewport = gd.CurrentViewports[0];
        var restore = gd.CurrentScissors.Length > 0 ? gd.CurrentScissors[0] : scissor;
        var region = open.Region;

        gd.SetScissors(scissor);
        gd.ColorComponentFlags = ColorComponentFlagBits.RBit | ColorComponentFlagBits.GBit |
                                 ColorComponentFlagBits.BBit | ColorComponentFlagBits.ABit;
        gd.ColorBlendEnabled = true;
        gd.ColorBlendEquation = ColorBlendEquations.Premultiplied;
        gd.DepthTestEnabled = false;
        gd.DepthWriteEnable = false;
        gd.PrimitiveTopology = PrimitiveTopology.TriangleStrip;
        gd.VertexType = null;

        // The mirrored layer: flipped about the floor halfway down the gap, moved sideways by its own offset.
        var floor = mirror.Floor;
        var mirroredLeft = region.Offset.X + mirror.OffsetX;
        var mirroredTop = 2f * floor - (region.Offset.Y + region.Extent.Height);
        var left = role > 1.5f ? mirroredLeft : region.Offset.X;
        var top = role > 1.5f ? mirroredTop : region.Offset.Y;
        _composite.LayerBox.SetValue(new Vector4F(left - viewport.X, top - viewport.Y,
            region.Extent.Width, region.Extent.Height));
        _composite.SourceRegion.SetValue(new Vector4F(region.Offset.X, region.Offset.Y, region.Extent.Width,
            region.Extent.Height));
        _composite.Mirror.SetValue(new Vector4F(mirror.Opacity, mirror.Begins, mirror.Ends, floor));
        _composite.MirrorFade.SetValue(new Vector4F(mirror.FadeLength, 0f, 0f, role));
        _composite.MirrorQuad.SetValue(new Vector4F(mirroredLeft, mirroredLeft + region.Extent.Width,
            mirroredTop + region.Extent.Height, Math.Abs(mirror.OffsetX) * 0.5f));
        _composite.ClipBox.SetValue(clipBox);
        _composite.ClipRadii.SetValue(clipRadii);
        _composite.SoftOn.SetValue(soft != null ? 1f : 0f);
        _composite.SoftTexture.SetResource(soft ?? open.Color);
        _composite.BandsOn.SetValue(bands != null ? 1f : 0f);
        _composite.BandsTexture.SetResource(bands ?? open.Color);
        _composite.LayerTexels.SetValue(new Vector4F(
            region.Extent.Width / (float)open.TextureWidth, region.Extent.Height / (float)open.TextureHeight, 0, 0));
        _composite.LayerAlpha.SetValue(alpha);
        _composite.ViewportSize.SetValue(new Vector2F(viewport.Width, viewport.Height));
        _composite.TargetOrigin.SetValue(new Vector2F(gd.TargetOrigin.X, gd.TargetOrigin.Y));
        _composite.MaskType.SetValue(mask.Type);
        _composite.MaskGeom0.SetValue(mask.Geom0);
        _composite.MaskGeom1.SetValue(mask.Geom1);
        _composite.MaskU.SetValue(mask.U);
        _composite.MaskV.SetValue(mask.V);
        _composite.MaskW.SetValue(mask.W);
        _composite.MaskAlpha0.SetValue(mask.Alpha0);
        _composite.MaskAlpha1.SetValue(mask.Alpha1);
        _composite.MaskOffsets0.SetValue(mask.Offsets0);
        _composite.MaskOffsets1.SetValue(mask.Offsets1);
        _composite.MaskParams.SetValue(mask.Params);
        _composite.SourceTexture.SetResource(open.Color);
        _composite.SourceSampler.SetResource(SamplerStates.LinearClampToEdge);
        _composite.LayerCompositeDrawPass.Apply();
        gd.Draw(4, 1);
        gd.SetScissors(restore);
    }

    private static void EnsureRing(GraphicsDevice gd, CachedLayer entry, uint width, uint height)
    {
        var count = Math.Max(1, (int)gd.MaxFramesInFlight) + 1;
        if (entry.Ring.Length == count && entry.TextureWidth >= width && entry.TextureHeight >= height)
        {
            return;
        }

        Release(gd, entry.Ring);
        entry.TextureWidth = Math.Max(entry.TextureWidth, width);
        entry.TextureHeight = Math.Max(entry.TextureHeight, height);
        entry.Ring = new IRenderTarget[count];
        entry.Latest = -1;
        entry.Valid = false;
        var format = gd.CurrentRenderTarget.SurfaceFormat;
        for (var i = 0; i < count; i++)
        {
            entry.Ring[i] = gd.CreateRenderTarget(entry.TextureWidth, entry.TextureHeight, MSAALevel.None, format,
                ImageUsageFlagBits.SampledBit | ImageUsageFlagBits.TransferSrcBit, ImageLayout.ShaderReadOnlyOptimal,
                $"Layer:{i}");
        }
    }

    private OffscreenLayerLevel LevelAt(GraphicsDevice gd, int depth, uint width, uint height)
    {
        while (_levels.Count <= depth)
        {
            _levels.Add(new OffscreenLayerLevel());
        }

        var level = _levels[depth];
        var frames = Math.Max(1, (int)gd.MaxFramesInFlight);
        var depthFormat = gd.CurrentDepthStencilBuffer is Texture { } windowDepth
            ? (DepthFormat)windowDepth.Description.Format
            : (DepthFormat?)null;
        if (!depthFormat.HasValue
            || (level.Depth.Length == frames && level.Width >= width && level.Height >= height))
        {
            return level;
        }

        Release(gd, level.Depth);
        level.Width = Math.Max(level.Width, width);
        level.Height = Math.Max(level.Height, height);
        level.Depth = new IDepthStencilBuffer[frames];
        for (var i = 0; i < frames; i++)
        {
            level.Depth[i] = gd.CreateDepthBuffer(level.Width, level.Height, depthFormat.Value, MSAALevel.None,
                name: $"LayerDepth{depth}:{i}");
        }

        return level;
    }

    private static void Release<T>(GraphicsDevice gd, T[] resources)
    {
        foreach (var resource in resources)
        {
            if (resource is IDisposable disposable)
            {
                gd.AddToDeferDisposeQueue(disposable);
            }
        }
    }

    public void Dispose()
    {
        foreach (var level in _levels)
        {
            foreach (var depth in level.Depth)
            {
                (depth as IDisposable)?.Dispose();
            }
        }

        foreach (var entry in _cached.Values)
        {
            foreach (var color in entry.Ring)
            {
                (color as IDisposable)?.Dispose();
            }

            foreach (var soft in entry.SoftRing ?? [])
            {
                (soft as IDisposable)?.Dispose();
            }

            foreach (var bands in entry.BandsRing ?? [])
            {
                (bands as IDisposable)?.Dispose();
            }

            (entry.SoftScratch as IDisposable)?.Dispose();
        }

        _levels.Clear();
        _cached.Clear();
        _composite?.Dispose();
        _composite = null;
        _soft?.Dispose();
        _soft = null;
    }
}
