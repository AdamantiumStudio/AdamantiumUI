using System;
using System.Collections.Generic;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Media;
using Adamantium.Vulkan.Core;

namespace Adamantium.UI.Rendering;

internal sealed class OffscreenLayers : IDisposable
{
    private readonly List<OffscreenLayerLevel> _levels = [];
    private readonly Stack<OpenOffscreenLayer> _open = new();
    private FX.LayerCompositeEffect _composite;

    /// <summary>Opens a layer over <paramref name="region"/> (device pixels of the window): what is drawn until
    /// <see cref="End"/> lands in it, at the same coordinates. A layer with nothing to draw into (an empty region) is still
    /// opened, drawing nowhere, so every <see cref="End"/> closes the layer it belongs to.</summary>
    public void Begin(IGraphicsDevice device, Rect2D region)
    {
        if (device is not GraphicsDevice gd || gd.CurrentRenderTarget == null
            || region.Extent.Width == 0 || region.Extent.Height == 0)
        {
            _open.Push(default);
            return;
        }

        var level = LevelAt(gd, _open.Count, region.Extent.Width, region.Extent.Height);
        var frame = (int)(gd.CurrentFrame % (uint)level.Color.Length);
        var color = level.Color[frame];
        var depth = level.Depth.Length > 0 ? level.Depth[frame] : null;

        _open.Push(new OpenOffscreenLayer(gd.CurrentRenderTarget, gd.CurrentDepthStencilBuffer, gd.TargetOrigin, gd.ClearColor,
            gd.MSAALevel, color, level.Width, level.Height, region));

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

        _composite ??= new FX.LayerCompositeEffect(gd);
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

    private OffscreenLayerLevel LevelAt(GraphicsDevice gd, int depth, uint width, uint height)
    {
        while (_levels.Count <= depth)
        {
            _levels.Add(new OffscreenLayerLevel());
        }

        var level = _levels[depth];
        var frames = Math.Max(1, (int)gd.MaxFramesInFlight);
        if (level.Color.Length == frames && level.Width >= width && level.Height >= height)
        {
            return level;
        }

        Release(gd, level);
        level.Width = Math.Max(level.Width, width);
        level.Height = Math.Max(level.Height, height);
        var format = gd.CurrentRenderTarget.SurfaceFormat;
        var depthFormat = gd.CurrentDepthStencilBuffer is Texture { } windowDepth
            ? (DepthFormat)windowDepth.Description.Format
            : (DepthFormat?)null;

        level.Color = new IRenderTarget[frames];
        level.Depth = depthFormat.HasValue ? new IDepthStencilBuffer[frames] : [];
        for (var i = 0; i < frames; i++)
        {
            level.Color[i] = gd.CreateRenderTarget(level.Width, level.Height, MSAALevel.None, format,
                ImageUsageFlagBits.SampledBit | ImageUsageFlagBits.TransferSrcBit, ImageLayout.ShaderReadOnlyOptimal,
                $"Layer{depth}:{i}");
            if (depthFormat.HasValue)
            {
                level.Depth[i] = gd.CreateDepthBuffer(level.Width, level.Height, depthFormat.Value, MSAALevel.None,
                    name: $"LayerDepth{depth}:{i}");
            }
        }

        return level;
    }

    private static void Release(GraphicsDevice gd, OffscreenLayerLevel level)
    {
        foreach (var color in level.Color)
        {
            if (color is IDisposable disposable)
            {
                gd.AddToDeferDisposeQueue(disposable);
            }
        }

        foreach (var depth in level.Depth)
        {
            if (depth is IDisposable disposable)
            {
                gd.AddToDeferDisposeQueue(disposable);
            }
        }

        level.Color = [];
        level.Depth = [];
    }

    public void Dispose()
    {
        foreach (var level in _levels)
        {
            foreach (var color in level.Color)
            {
                (color as IDisposable)?.Dispose();
            }

            foreach (var depth in level.Depth)
            {
                (depth as IDisposable)?.Dispose();
            }
        }

        _levels.Clear();
        _composite?.Dispose();
        _composite = null;
    }
}
