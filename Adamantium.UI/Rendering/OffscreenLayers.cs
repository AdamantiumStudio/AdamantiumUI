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
    private long _frame;

    /// <summary>How many layers this frame drew their subtree into their texture.</summary>
    public int Redrawn { get; private set; }

    /// <summary>How many layers this frame laid down the texture they already held.</summary>
    public int Reused { get; private set; }

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
            Release(gd, _cached[owner].Ring);
            _cached.Remove(owner);
        }

        _idle.Clear();
    }

    /// <summary>Opens <paramref name="owner"/>'s layer over <paramref name="region"/> (window device pixels). True means
    /// its texture already holds a drawing with this <paramref name="print"/>: draw nothing until <see cref="End"/>, which
    /// lays it down. Otherwise drawing lands in a fresh texture, kept for reuse when <paramref name="reusable"/>.</summary>
    public bool Begin(IGraphicsDevice device, object owner, Rect2D region, ulong print, bool reusable, bool allowReuse)
    {
        if (device is not GraphicsDevice gd || gd.CurrentRenderTarget == null
            || region.Extent.Width == 0 || region.Extent.Height == 0)
        {
            _open.Push(default);
            return false;
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
            return true;
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
        return false;
    }

    /// <summary>Closes the innermost layer, goes back to the target it was opened over and lays the layer down there with
    /// <paramref name="alpha"/> and <paramref name="mask"/>, cut by <paramref name="scissor"/> (device pixels of the
    /// window).</summary>
    public void End(IGraphicsDevice device, float alpha, Rect2D scissor, in LayerMask mask)
    {
        if (device is not GraphicsDevice gd || _open.Count == 0)
        {
            return;
        }

        var open = _open.Pop();
        if (open.Color == null)
        {
            return;
        }

        if (!open.Reused)
        {
            gd.SuspendRendering();
            gd.InsertImageMemoryBarrier(gd.CurrentCommandBuffer, open.Color,
                AccessFlagBits2.ColorAttachmentWriteBit, AccessFlagBits2.ShaderReadBit,
                ImageLayout.ColorAttachmentOptimal, ImageLayout.ShaderReadOnlyOptimal,
                PipelineStageFlagBits2.ColorAttachmentOutputBit, PipelineStageFlagBits2.FragmentShaderBit);

            gd.SetRenderTargets(open.Target);
            gd.SetDepthBuffer(open.Depth);
            gd.ClearColor = open.Clear;
            gd.MSAALevel = open.Samples;
            gd.TargetOrigin = open.Origin;
            gd.ResumeRendering();

            var entry = open.Entry;
            entry.Latest = open.Slot;
            entry.Print = open.Print;
            entry.Valid = open.Reusable;
            entry.Width = open.Region.Extent.Width;
            entry.Height = open.Region.Extent.Height;
        }

        if (alpha > 0f)
        {
            Composite(gd, open, alpha, scissor, mask);
        }
    }

    private void Composite(GraphicsDevice gd, OpenOffscreenLayer open, float alpha, Rect2D scissor, in LayerMask mask)
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

        _composite.LayerBox.SetValue(new Vector4F(region.Offset.X - viewport.X, region.Offset.Y - viewport.Y,
            region.Extent.Width, region.Extent.Height));
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
        }

        _levels.Clear();
        _cached.Clear();
        _composite?.Dispose();
        _composite = null;
    }
}
