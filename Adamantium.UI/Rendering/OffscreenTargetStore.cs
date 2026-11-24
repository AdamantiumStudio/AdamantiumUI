using System;
using System.Collections.Generic;
using Adamantium.Graphics.Core;
using Adamantium.Vulkan.Core;

namespace Adamantium.UI.Rendering;

/// <summary>The targets groups and masks are drawn into, and the depth of each level of layers open, for one pass: a
/// frame of the window, or one drawing of a raster's texture. What a pass did not need is let go when it ends - not
/// after a while: a raster waiting for the camera to stop passes frames of its own, and whatever was counted in frames or
/// seconds was let go just before its next drawing and made again inside it.</summary>
internal sealed class OffscreenTargetStore
{
    private readonly List<StoredTarget> _free = [];
    private readonly List<OffscreenLayerLevel> _levels = [];

    // Sizes are taken up to a step, so a group a few pixels larger than the last does not make a texture of its own.
    private const uint Step = 256;

    internal sealed class StoredTarget
    {
        public IRenderTarget Texture;
        public uint Width;
        public uint Height;
        public bool Needed;
        public OffscreenTargetStore Store;
    }

    /// <summary>The smallest target held that takes <paramref name="width"/> by <paramref name="height"/>, or a new one of
    /// that size: made over to hold both, a tall one and a wide one took a square texture as large as both together.</summary>
    public StoredTarget Take(IGraphicsDevice gd, uint width, uint height)
    {
        var pick = -1;
        for (var i = 0; i < _free.Count; i++)
        {
            var free = _free[i];
            if (free.Width >= width && free.Height >= height
                && (pick < 0 || (ulong)free.Width * free.Height < (ulong)_free[pick].Width * _free[pick].Height))
            {
                pick = i;
            }
        }

        StoredTarget target;
        if (pick >= 0)
        {
            target = _free[pick];
            _free.RemoveAt(pick);
        }
        else
        {
            target = new StoredTarget { Width = StepUp(width), Height = StepUp(height), Store = this };
            target.Texture = gd.CreateRenderTarget(target.Width, target.Height, MSAALevel.None,
                gd.CurrentRenderTarget.SurfaceFormat, ImageUsageFlagBits.SampledBit | ImageUsageFlagBits.TransferSrcBit,
                ImageLayout.ShaderReadOnlyOptimal, "GroupTarget");
        }

        target.Needed = true;
        return target;
    }

    /// <summary>Back to the store: a later group of the pass may draw into it, the device keeping the order of the two.
    /// </summary>
    public void GiveBack(StoredTarget target) => _free.Add(target);

    /// <summary>The depth of level <paramref name="depth"/> of layers open, at least <paramref name="width"/> by
    /// <paramref name="height"/>, one a frame in flight.</summary>
    public OffscreenLayerLevel LevelAt(IGraphicsDevice gd, int depth, uint width, uint height)
    {
        while (_levels.Count <= depth)
        {
            _levels.Add(new OffscreenLayerLevel());
        }

        var level = _levels[depth];
        level.AskedWidth = Math.Max(level.AskedWidth, width);
        level.AskedHeight = Math.Max(level.AskedHeight, height);
        var frames = Math.Max(1, (int)gd.MaxFramesInFlight);
        var depthFormat = gd.CurrentDepthStencilBuffer is Graphics.Texture { } windowDepth
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

    /// <summary>The pass is over: what it did not take goes, and depth four times larger than it asked is made smaller
    /// when next asked. <paramref name="taken"/> - targets still out, a mask whose group drew nothing - come back first.
    /// </summary>
    public void EndPass(IGraphicsDevice gd, List<StoredTarget> taken)
    {
        for (var i = taken.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(taken[i].Store, this))
            {
                _free.Add(taken[i]);
                taken.RemoveAt(i);
            }
        }

        for (var i = _free.Count - 1; i >= 0; i--)
        {
            if (!_free[i].Needed)
            {
                Dispose(gd, _free[i].Texture);
                _free.RemoveAt(i);
                continue;
            }

            _free[i].Needed = false;
        }

        foreach (var level in _levels)
        {
            if (level.Depth.Length > 0
                && (ulong)level.Width * level.Height > 4UL * level.AskedWidth * level.AskedHeight)
            {
                Release(gd, level.Depth);
                level.Depth = [];
                level.Width = 0;
                level.Height = 0;
            }

            level.AskedWidth = 0;
            level.AskedHeight = 0;
        }
    }

    /// <summary>Lets go of everything it holds, behind the frames that may still use it.</summary>
    public void Release(IGraphicsDevice gd)
    {
        foreach (var target in _free)
        {
            Dispose(gd, target.Texture);
        }

        _free.Clear();
        foreach (var level in _levels)
        {
            Release(gd, level.Depth);
        }

        _levels.Clear();
    }

    /// <summary>Destroys everything at once, the device idle.</summary>
    public void DisposeNow()
    {
        foreach (var target in _free)
        {
            (target.Texture as IDisposable)?.Dispose();
        }

        foreach (var level in _levels)
        {
            foreach (var depth in level.Depth)
            {
                (depth as IDisposable)?.Dispose();
            }
        }

        _free.Clear();
        _levels.Clear();
    }

    private static uint StepUp(uint size) => (size + Step - 1) / Step * Step;

    private static void Dispose(IGraphicsDevice gd, object resource)
    {
        if (resource is IDisposable disposable)
        {
            if (gd != null)
            {
                gd.AddToDeferDisposeQueue(disposable);
            }
            else
            {
                disposable.Dispose();
            }
        }
    }

    private static void Release<T>(IGraphicsDevice gd, T[] resources)
    {
        foreach (var resource in resources)
        {
            Dispose(gd, resource);
        }
    }
}
