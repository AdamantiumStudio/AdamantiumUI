using System;
using System.Collections.Generic;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.EffectsFramework;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.FX;
using Adamantium.Vulkan.Core;

namespace Adamantium.UI.Rendering;

internal sealed class OffscreenLayers : IDisposable
{
    private const long IdleFrames = 120;

    private readonly Stack<OpenOffscreenLayer> _open = new();
    private readonly Dictionary<object, CachedLayer> _cached = new();
    private readonly List<object> _idle = [];
    private LayerCompositeEffect _composite;
    private GroupCompositeEffect _group;
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
    public void BeginFrame(IGraphicsDevice gd)
    {
        _frame++;
        Redrawn = 0;
        Reused = 0;
        if (gd == null)
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

            idle.Store?.Release(gd);
            if (idle.Pending != null)
            {
                Release(gd, new[] { idle.Pending });
            }
            _cached.Remove(owner);
        }

        _idle.Clear();
        // The frame before was the window's pass: what it did not need goes.
        _windowStore.EndPass(gd, _takenGroupTargets);
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
    private DrawSpace EnterSpace(IGraphicsDevice gd, Rect2D region, Matrix4x4F projection, Rect2D bounds, bool whole)
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

    private void LeaveSpace(IGraphicsDevice gd)
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
    public DrawSpace BeginBands(IGraphicsDevice gd, object owner, Rect2D region, Matrix4x4F projection, Rect2D bounds,
        bool whole)
    {
        if (gd?.CurrentRenderTarget == null || region.Extent.Width == 0 || region.Extent.Height == 0)
        {
            _bands.Push(default);
            return EnterSpace(gd, region, projection, bounds, whole);
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
    public DrawSpace EndBands(IGraphicsDevice gd)
    {
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

    private static void EnsureBands(IGraphicsDevice gd, CachedLayer entry)
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
    public DrawSpace Begin(IGraphicsDevice gd, object owner, Rect2D region, ulong print, bool reusable,
        bool allowReuse, Matrix4x4F projection, Rect2D bounds, bool whole, out bool reused) =>
        Open(gd, owner, region, print, reusable, allowReuse, projection, bounds, whole, Colors.Transparent, out reused);

    /// <summary>Opens a target over <paramref name="region"/> (window device pixels), cleared to <paramref name="clear"/>:
    /// what a group of draws, or one of its masks, is drawn into, in the space it was opened in - its
    /// <paramref name="projection"/> and <paramref name="bounds"/>. Drawn afresh every frame; <see cref="EndGroupTarget"/>
    /// closes it and keeps what it holds for <see cref="LayDownGroup"/>, which gives it back.</summary>
    public DrawSpace BeginGroupTarget(IGraphicsDevice gd, Rect2D region, Color clear, Matrix4x4F projection, Rect2D bounds)
    {
        if (gd?.CurrentRenderTarget == null || region.Extent.Width == 0 || region.Extent.Height == 0)
        {
            _open.Push(default);
            return EnterSpace(gd, region, projection, bounds, false);
        }

        var target = TakeGroupTarget(gd, region.Extent.Width, region.Extent.Height);
        OpenOver(gd, target.Texture, target.Width, target.Height, region, null, 0, 0, false, clear);
        return EnterSpace(gd, region, projection, bounds, false);
    }

    // ---- GROUP TARGETS: drawn and laid down within one frame, so they are shared - taken from a store as a group opens
    // and given back when it is laid down. Kept per group, each held a ring of its own the size of what it covers, and a
    // page of hundreds of cut groups drawn as a raster held gigabytes. The window has a store, and so has each raster's
    // texture, for the groups inside it: each lets go of what its last pass did not need.

    private readonly OffscreenTargetStore _windowStore = new();

    // The store of the raster's texture being drawn now, or null.
    private OffscreenTargetStore _tileStore;

    private OffscreenTargetStore Store => _tileStore ?? _windowStore;

    // Targets out of their stores this frame.
    private readonly List<OffscreenTargetStore.StoredTarget> _takenGroupTargets = [];

    private OffscreenTargetStore.StoredTarget TakeGroupTarget(IGraphicsDevice gd, uint width, uint height)
    {
        var target = Store.Take(gd, width, height);
        _takenGroupTargets.Add(target);
        return target;
    }

    private void GiveBack(IRenderTarget texture)
    {
        if (texture == null)
        {
            return;
        }

        for (var i = 0; i < _takenGroupTargets.Count; i++)
        {
            if (ReferenceEquals(_takenGroupTargets[i].Texture, texture))
            {
                var target = _takenGroupTargets[i];
                _takenGroupTargets.RemoveAt(i);
                target.Store.GiveBack(target);
                return;
            }
        }
    }

    private DrawSpace Open(IGraphicsDevice gd, object owner, Rect2D region, ulong print, bool reusable,
        bool allowReuse, Matrix4x4F projection, Rect2D bounds, bool whole, Color clear, out bool reused)
    {
        reused = false;
        if (gd?.CurrentRenderTarget == null || region.Extent.Width == 0 || region.Extent.Height == 0)
        {
            _open.Push(default);
            return EnterSpace(gd, region, projection, bounds, whole);
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
        OpenOver(gd, entry.Ring[slot], entry.TextureWidth, entry.TextureHeight, region, entry, slot, print, reusable, clear);
        return EnterSpace(gd, region, projection, bounds, whole);
    }

    // Draws from here into color - textureWidth by textureHeight, holding region at its corner - cleared to clear.
    private void OpenOver(IGraphicsDevice gd, IRenderTarget color, uint textureWidth, uint textureHeight, Rect2D region,
        CachedLayer entry, int slot, ulong print, bool reusable, Color clear)
    {
        var level = LevelAt(gd, _open.Count, textureWidth, textureHeight);
        var depth = level.Depth.Length > 0 ? level.Depth[(int)(gd.CurrentFrame % (uint)level.Depth.Length)] : null;

        _open.Push(new OpenOffscreenLayer(gd.CurrentRenderTarget, gd.CurrentDepthStencilBuffer, gd.TargetOrigin, gd.ClearColor,
            gd.MSAALevel, color, textureWidth, textureHeight, region, entry, slot, print, reusable, false));

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
        gd.ClearColor = clear;
        gd.MSAALevel = MSAALevel.None;
        gd.BeginRendering(commandBuffer);
        gd.TargetOrigin = new NativePoint(region.Offset.X, region.Offset.Y);
        Redrawn++;
    }

    /// <summary>Closes the target <see cref="BeginGroupTarget"/> opened and goes back to the one, and the space, it was
    /// opened over; what it holds stays for <see cref="LayDownGroup"/> this frame.</summary>
    public GroupTarget EndGroupTarget(IGraphicsDevice gd)
    {
        var open = _open.Pop();
        if (gd == null || open.Color == null)
        {
            LeaveSpace(gd);
            return default;
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
        LeaveSpace(gd);
        return new GroupTarget(open.Color, open.Region, open.TextureWidth, open.TextureHeight);
    }

    /// <summary>Lays a group down on the current target, cut by <paramref name="scissor"/> (window device pixels): its
    /// <paramref name="content"/> through up to three <paramref name="masks"/> (by alpha, or by luminosity), faded to
    /// <paramref name="alpha"/>, mixed by <paramref name="blendMode"/> with <paramref name="backdrop"/> - a copy of what
    /// lies under it, which every mode but Normal reads. <paramref name="replaces"/>: the content started as that copy,
    /// and takes its place where the masks let it through.</summary>
    public void LayDownGroup(IGraphicsDevice gd, in GroupTarget content, ReadOnlySpan<(GroupTarget Target, bool Luminosity)> masks,
        GroupBackdrop backdrop, BlendMode blendMode, float alpha, Rect2D scissor, bool replaces = false)
    {
        Composite(gd, content, masks, backdrop, blendMode, alpha, scissor, replaces);
        GiveBack(content.Texture);
        foreach (var (mask, _) in masks)
        {
            GiveBack(mask.Texture);
        }
    }

    private void Composite(IGraphicsDevice gd, in GroupTarget content, ReadOnlySpan<(GroupTarget Target, bool Luminosity)> masks,
        GroupBackdrop backdrop, BlendMode blendMode, float alpha, Rect2D scissor, bool replaces)
    {
        if (gd == null || !content.Drawn || gd.CurrentViewports.Length == 0 || scissor.Extent.Width == 0
            || scissor.Extent.Height == 0)
        {
            return;
        }

        _group ??= new GroupCompositeEffect(gd);
        var viewport = gd.CurrentViewports[0];
        var restore = gd.CurrentScissors.Length > 0 ? gd.CurrentScissors[0] : scissor;
        var region = content.Region;
        replaces &= backdrop?.Image != null;
        // Without a copy of what lies under it a mode has nothing to mix with: the group is laid over it instead.
        var normal = !replaces && (blendMode == BlendMode.Normal || backdrop?.Image == null);

        gd.SetScissors(scissor);
        gd.ColorComponentFlags = ColorComponentFlagBits.RBit | ColorComponentFlagBits.GBit |
                                 ColorComponentFlagBits.BBit | ColorComponentFlagBits.ABit;
        gd.ColorBlendEnabled = normal;
        gd.ColorBlendEquation = ColorBlendEquations.Premultiplied;
        gd.DepthTestEnabled = false;
        gd.DepthWriteEnable = false;
        gd.PrimitiveTopology = PrimitiveTopology.TriangleStrip;
        gd.VertexType = null;

        _group.LayerBox.SetValue(new Vector4F(region.Offset.X - viewport.X, region.Offset.Y - viewport.Y,
            region.Extent.Width, region.Extent.Height));
        _group.SourceRegion.SetValue(new Vector4F(region.Offset.X, region.Offset.Y, region.Extent.Width,
            region.Extent.Height));
        var texels = content.Texels;
        _group.LayerTexels.SetValue(new Vector4F(texels.Width, texels.Height, 0, 0));
        SetMask(_group.Mask0Texture, _group.Mask0, masks, 0, content);
        SetMask(_group.Mask1Texture, _group.Mask1, masks, 1, content);
        SetMask(_group.Mask2Texture, _group.Mask2, masks, 2, content);
        var under = !normal && backdrop?.Image != null;
        if (under)
        {
            _group.BackdropTexture.SetResource(backdrop.Image);
        }
        else
        {
            _group.BackdropTexture.SetResource(content.Texture);
        }

        _group.Backdrop.SetValue(under
            ? new Vector4F(backdrop.Texels.Width, backdrop.Texels.Height, 0, 0)
            : new Vector4F(texels.Width, texels.Height, 0, 0));
        _group.GroupAlpha.SetValue(alpha);
        _group.BlendMode.SetValue(replaces ? ReplaceMode : normal ? 0f : (float)blendMode);
        _group.ViewportSize.SetValue(new Vector2F(viewport.Width, viewport.Height));
        _group.TargetOrigin.SetValue(new Vector2F(gd.TargetOrigin.X, gd.TargetOrigin.Y));
        _group.SourceTexture.SetResource(content.Texture);
        _group.SourceSampler.SetResource(SamplerStates.LinearClampToEdge);
        _group.GroupCompositeDrawPass.Apply();
        gd.Draw(4, 1);
        gd.ColorBlendEnabled = true;
        gd.SetScissors(restore);
    }

    // The composite's mode past BlendMode's: the content in place of the copy of what lay under it, by the masks.
    private const float ReplaceMode = 16f;

    // ---- THE TEXTURE of an element drawn as a raster (DrawingMode.Raster) ------------------------------------------

    /// <summary>Whether the texture kept under <paramref name="key"/> holds a drawing with this <paramref name="print"/>.
    /// </summary>
    public bool HoldsTile(object key, ulong print) =>
        _cached.TryGetValue(key, out var entry) && entry.Valid && entry.Latest >= 0 && entry.Print == print;

    /// <summary>Whether the texture kept under <paramref name="key"/> holds a drawing, whatever its print.</summary>
    public bool HoldsAnyTile(object key) => _cached.TryGetValue(key, out var entry) && entry.Valid && entry.Latest >= 0;

    // ---- Drawn anew square by square: a texture beside the one laid down, each square drawn into a small target of its
    // own and pasted into place, the texture laid down meanwhile until the last square is in.

    // The small target the square being drawn goes into.
    private OffscreenTargetStore.StoredTarget _square;

    /// <summary>Whether the texture being drawn anew under <paramref name="key"/> is drawn for <paramref name="print"/>.
    /// </summary>
    public bool DrawsAnew(object key, ulong print) =>
        _cached.TryGetValue(key, out var entry) && entry.Pending != null && entry.PendingPrint == print;

    /// <summary>Starts drawing anew the texture kept under <paramref name="key"/>: <paramref name="width"/> by
    /// <paramref name="height"/> texels holding <paramref name="print"/>, beside the one laid down.</summary>
    public void StartDrawingAnew(IGraphicsDevice gd, object key, ulong print, int width, int height)
    {
        if (!_cached.TryGetValue(key, out var entry))
        {
            entry = new CachedLayer();
            _cached[key] = entry;
        }

        entry.Store ??= new OffscreenTargetStore();
        if (entry.Pending != null && (entry.PendingWidth != width || entry.PendingHeight != height))
        {
            Release(gd, new[] { entry.Pending });
            entry.Pending = null;
        }

        if (entry.Pending == null)
        {
            entry.Pending = gd.CreateRenderTarget((uint)width, (uint)height, MSAALevel.None,
                gd.CurrentRenderTarget.SurfaceFormat, ImageUsageFlagBits.SampledBit | ImageUsageFlagBits.TransferSrcBit,
                ImageLayout.ShaderReadOnlyOptimal, "RasterAnew");
            entry.PendingWidth = (uint)width;
            entry.PendingHeight = (uint)height;
        }

        entry.PendingCleared = false;
        entry.PendingPrint = print;
        entry.LastUsed = _frame;
    }

    /// <summary>Opens a square of <paramref name="width"/> by <paramref name="height"/> texels of the texture being drawn
    /// anew under <paramref name="key"/>, drawn in its own space: what is drawn from here goes into it, up to
    /// <see cref="EndSquare"/>, which goes back to the space it was opened in - its <paramref name="projection"/> and
    /// <paramref name="bounds"/>. What is drawn into it - depth, the groups inside - comes from the texture's own store.
    /// </summary>
    public DrawSpace BeginSquare(IGraphicsDevice gd, object key, Matrix4x4F projection, Rect2D bounds, int width, int height)
    {
        var entry = _cached[key];
        _tileStore = entry.Store;
        var region = new Rect2D
            { Offset = new Offset2D(), Extent = new Extent2D { Width = (uint)width, Height = (uint)height } };
        if (gd?.CurrentRenderTarget == null)
        {
            _open.Push(default);
            return EnterSpace(gd, region, projection, bounds, true);
        }

        _square = _tileStore.Take(gd, (uint)width, (uint)height);
        OpenOver(gd, _square.Texture, _square.Width, _square.Height, region, null, 0, 0, false, Colors.Transparent);
        return EnterSpace(gd, region, projection, bounds, true);
    }

    /// <summary>Closes the square <see cref="BeginSquare"/> opened and pastes it at <paramref name="at"/>, texels of the
    /// texture being drawn anew; gives back the space the square was opened in.</summary>
    public DrawSpace EndSquare(IGraphicsDevice gd, object key, Rect2D at)
    {
        var open = _open.Pop();
        var entry = _cached[key];
        if (gd != null && open.Color != null)
        {
            gd.SuspendRendering();
            var commands = gd.CurrentCommandBuffer;
            gd.InsertImageMemoryBarrier(commands, open.Color,
                AccessFlagBits2.ColorAttachmentWriteBit, AccessFlagBits2.ShaderReadBit,
                ImageLayout.ColorAttachmentOptimal, ImageLayout.ShaderReadOnlyOptimal,
                PipelineStageFlagBits2.ColorAttachmentOutputBit, PipelineStageFlagBits2.FragmentShaderBit);

            // Into the texture being drawn anew, what is in it kept - cleared once, when its first square goes in.
            gd.InsertImageMemoryBarrier(commands, entry.Pending,
                AccessFlagBits2.ShaderReadBit, AccessFlagBits2.ColorAttachmentReadBit | AccessFlagBits2.ColorAttachmentWriteBit,
                entry.PendingCleared ? ImageLayout.ShaderReadOnlyOptimal : ImageLayout.Undefined,
                ImageLayout.ColorAttachmentOptimal,
                PipelineStageFlagBits2.FragmentShaderBit, PipelineStageFlagBits2.ColorAttachmentOutputBit);
            var viewports = gd.CurrentViewports.ToArray();
            gd.SetRenderTargets(entry.Pending);
            gd.SetDepthBuffer(null);
            gd.ClearColor = Colors.Transparent;
            gd.MSAALevel = MSAALevel.None;
            gd.TargetOrigin = new NativePoint(0, 0);
            gd.SetViewports(new Viewport { Width = entry.PendingWidth, Height = entry.PendingHeight, MinDepth = 0, MaxDepth = 1 });
            gd.BeginRendering(commands, continueRendering: entry.PendingCleared);
            entry.PendingCleared = true;
            Paste(gd, open.Color, open.TextureWidth, open.TextureHeight, at, entry.PendingWidth, entry.PendingHeight);
            gd.SuspendRendering();
            gd.InsertImageMemoryBarrier(gd.CurrentCommandBuffer, entry.Pending,
                AccessFlagBits2.ColorAttachmentWriteBit, AccessFlagBits2.ShaderReadBit,
                ImageLayout.ColorAttachmentOptimal, ImageLayout.ShaderReadOnlyOptimal,
                PipelineStageFlagBits2.ColorAttachmentOutputBit, PipelineStageFlagBits2.FragmentShaderBit);

            gd.SetViewports(viewports);
            gd.SetRenderTargets(open.Target);
            gd.SetDepthBuffer(open.Depth);
            gd.ClearColor = open.Clear;
            gd.MSAALevel = open.Samples;
            gd.TargetOrigin = open.Origin;
            gd.ResumeRendering();
        }

        if (_square != null)
        {
            _tileStore.GiveBack(_square);
            _square = null;
        }

        _tileStore = null;
        var outer = _spaces.Peek().Outer;
        LeaveSpace(gd);
        return outer;
    }

    // The square, texel for texel, at its place: nothing under it mixed in.
    private void Paste(IGraphicsDevice gd, IRenderTarget square, uint squareWidth, uint squareHeight, Rect2D at,
        uint width, uint height)
    {
        _group ??= new GroupCompositeEffect(gd);
        gd.SetScissors(at);
        gd.ColorComponentFlags = ColorComponentFlagBits.RBit | ColorComponentFlagBits.GBit |
                                 ColorComponentFlagBits.BBit | ColorComponentFlagBits.ABit;
        gd.ColorBlendEnabled = false;
        gd.DepthTestEnabled = false;
        gd.DepthWriteEnable = false;
        gd.PrimitiveTopology = PrimitiveTopology.TriangleStrip;
        gd.VertexType = null;

        var box = new Vector4F(at.Offset.X, at.Offset.Y, at.Extent.Width, at.Extent.Height);
        var texels = new Vector4F(at.Extent.Width / (float)squareWidth, at.Extent.Height / (float)squareHeight, 0, 0);
        _group.LayerBox.SetValue(box);
        _group.SourceRegion.SetValue(box);
        _group.LayerTexels.SetValue(texels);
        _group.Mask0.SetValue(Vector4F.Zero);
        _group.Mask1.SetValue(Vector4F.Zero);
        _group.Mask2.SetValue(Vector4F.Zero);
        _group.Mask0Texture.SetResource(square);
        _group.Mask1Texture.SetResource(square);
        _group.Mask2Texture.SetResource(square);
        _group.BackdropTexture.SetResource(square);
        _group.Backdrop.SetValue(texels);
        _group.GroupAlpha.SetValue(1f);
        _group.BlendMode.SetValue(0f);
        _group.ViewportSize.SetValue(new Vector2F(width, height));
        _group.TargetOrigin.SetValue(new Vector2F(0, 0));
        _group.SourceTexture.SetResource(square);
        _group.SourceSampler.SetResource(SamplerStates.LinearClampToEdge);
        _group.GroupCompositeDrawPass.Apply();
        gd.Draw(4, 1);
        gd.ColorBlendEnabled = true;
    }

    /// <summary>Lets go of the texture being drawn anew under <paramref name="key"/>, if one is: what it was drawn for is
    /// held again.</summary>
    public void StopDrawingAnew(IGraphicsDevice gd, object key)
    {
        if (gd == null || !_cached.TryGetValue(key, out var entry) || entry.Pending == null)
        {
            return;
        }

        Release(gd, new[] { entry.Pending });
        entry.Pending = null;
        entry.Store?.EndPass(gd, _takenGroupTargets);
    }

    /// <summary>The last square of the texture drawn anew under <paramref name="key"/> is in: it is the one laid down from
    /// now, the one before let go, and what the drawing did not need of its store goes.</summary>
    public void FinishDrawingAnew(IGraphicsDevice gd, object key)
    {
        var entry = _cached[key];
        Release(gd, entry.Ring);
        entry.Ring = new[] { entry.Pending };
        entry.Latest = 0;
        entry.Valid = true;
        entry.Print = entry.PendingPrint;
        entry.TextureWidth = entry.Width = entry.PendingWidth;
        entry.TextureHeight = entry.Height = entry.PendingHeight;
        entry.Pending = null;
        entry.Store?.EndPass(gd, _takenGroupTargets);
    }

    /// <summary>Lays the tile kept under <paramref name="key"/> over <paramref name="box"/> - window device pixels, not
    /// whole ones: the camera puts a tile anywhere and at any scale - stretched to it, cut by <paramref name="scissor"/>.
    /// False when it holds no drawing.</summary>
    public bool LayDownTile(IGraphicsDevice gd, object key, Vector4F box, Rect2D scissor, float alpha = 1f)
    {
        if (gd == null || gd.CurrentViewports.Length == 0 || !_cached.TryGetValue(key, out var entry) || !entry.Valid
            || entry.Latest < 0 || scissor.Extent.Width == 0 || scissor.Extent.Height == 0)
        {
            return false;
        }

        entry.LastUsed = _frame;
        _group ??= new GroupCompositeEffect(gd);
        var viewport = gd.CurrentViewports[0];
        var restore = gd.CurrentScissors.Length > 0 ? gd.CurrentScissors[0] : scissor;
        var texture = entry.Ring[entry.Latest];

        gd.SetScissors(scissor);
        gd.ColorComponentFlags = ColorComponentFlagBits.RBit | ColorComponentFlagBits.GBit |
                                 ColorComponentFlagBits.BBit | ColorComponentFlagBits.ABit;
        gd.ColorBlendEnabled = true;
        gd.ColorBlendEquation = ColorBlendEquations.Premultiplied;
        gd.DepthTestEnabled = false;
        gd.DepthWriteEnable = false;
        gd.PrimitiveTopology = PrimitiveTopology.TriangleStrip;
        gd.VertexType = null;

        var texels = new Vector4F((float)entry.Width / entry.TextureWidth, (float)entry.Height / entry.TextureHeight, 0, 0);
        _group.LayerBox.SetValue(new Vector4F(box.X - viewport.X, box.Y - viewport.Y, box.Z, box.W));
        _group.SourceRegion.SetValue(box);
        _group.LayerTexels.SetValue(texels);
        _group.Mask0.SetValue(Vector4F.Zero);
        _group.Mask1.SetValue(Vector4F.Zero);
        _group.Mask2.SetValue(Vector4F.Zero);
        _group.Mask0Texture.SetResource(texture);
        _group.Mask1Texture.SetResource(texture);
        _group.Mask2Texture.SetResource(texture);
        _group.BackdropTexture.SetResource(texture);
        _group.Backdrop.SetValue(texels);
        _group.GroupAlpha.SetValue(alpha);
        _group.BlendMode.SetValue(0f);
        _group.ViewportSize.SetValue(new Vector2F(viewport.Width, viewport.Height));
        _group.TargetOrigin.SetValue(new Vector2F(gd.TargetOrigin.X, gd.TargetOrigin.Y));
        _group.SourceTexture.SetResource(texture);
        _group.SourceSampler.SetResource(SamplerStates.LinearClampToEdge);
        _group.GroupCompositeDrawPass.Apply();
        gd.Draw(4, 1);
        gd.SetScissors(restore);
        return true;
    }

    /// <summary>Fills the group target just opened over <paramref name="region"/> with the copy of what lay under it.
    /// </summary>
    public void FillFromBackdrop(IGraphicsDevice gd, GroupBackdrop backdrop, Rect2D region)
    {
        if (gd == null || backdrop?.Image == null || gd.CurrentViewports.Length == 0 || region.Extent.Width == 0
            || region.Extent.Height == 0)
        {
            return;
        }

        _group ??= new GroupCompositeEffect(gd);
        var viewport = gd.CurrentViewports[0];
        var restore = gd.CurrentScissors.Length > 0 ? gd.CurrentScissors[0] : region;
        gd.SetScissors(region);
        gd.ColorComponentFlags = ColorComponentFlagBits.RBit | ColorComponentFlagBits.GBit |
                                 ColorComponentFlagBits.BBit | ColorComponentFlagBits.ABit;
        gd.ColorBlendEnabled = false;
        gd.DepthTestEnabled = false;
        gd.DepthWriteEnable = false;
        gd.PrimitiveTopology = PrimitiveTopology.TriangleStrip;
        gd.VertexType = null;

        var texels = backdrop.Texels;
        _group.LayerBox.SetValue(new Vector4F(region.Offset.X - viewport.X, region.Offset.Y - viewport.Y,
            region.Extent.Width, region.Extent.Height));
        _group.SourceRegion.SetValue(new Vector4F(region.Offset.X, region.Offset.Y, region.Extent.Width,
            region.Extent.Height));
        _group.LayerTexels.SetValue(new Vector4F(texels.Width, texels.Height, 0, 0));
        _group.Mask0Texture.SetResource(backdrop.Image);
        _group.Mask1Texture.SetResource(backdrop.Image);
        _group.Mask2Texture.SetResource(backdrop.Image);
        _group.Mask0.SetValue(Vector4F.Zero);
        _group.Mask1.SetValue(Vector4F.Zero);
        _group.Mask2.SetValue(Vector4F.Zero);

        _group.BackdropTexture.SetResource(backdrop.Image);
        _group.Backdrop.SetValue(new Vector4F(texels.Width, texels.Height, 0, 0));
        _group.GroupAlpha.SetValue(1f);
        _group.BlendMode.SetValue(0f);
        _group.ViewportSize.SetValue(new Vector2F(viewport.Width, viewport.Height));
        _group.TargetOrigin.SetValue(new Vector2F(gd.TargetOrigin.X, gd.TargetOrigin.Y));
        _group.SourceTexture.SetResource(backdrop.Image);
        _group.SourceSampler.SetResource(SamplerStates.LinearClampToEdge);
        _group.GroupCompositeDrawPass.Apply();
        gd.Draw(4, 1);
        gd.ColorBlendEnabled = true;
        gd.SetScissors(restore);
    }

    private static void SetMask(EffectParameter texture, EffectParameter info,
        ReadOnlySpan<(GroupTarget Target, bool Luminosity)> masks, int index, in GroupTarget content)
    {
        if (index >= masks.Length || !masks[index].Target.Drawn)
        {
            texture.SetResource(content.Texture);
            info.SetValue(Vector4F.Zero);
            return;
        }

        var (target, luminosity) = masks[index];
        var texels = target.Texels;
        texture.SetResource(target.Texture);
        info.SetValue(new Vector4F(texels.Width, texels.Height, 1f, luminosity ? 1f : 0f));
    }

    /// <summary>Closes the innermost layer, goes back to the target it was opened over and lays the layer down there with
    /// <paramref name="alpha"/> and <paramref name="mask"/>, faded <paramref name="soft"/> device pixels in from what it
    /// drew, cut by <paramref name="scissor"/> (device pixels of the window); and its reflection, cut by
    /// <paramref name="mirrorScissor"/>, where <paramref name="mirror"/> shows. <paramref name="softCut"/> marks the sides
    /// (left, top, right, bottom) where its ancestors cut what it drew short: the drawing goes on past them, so nothing
    /// fades there. Drawing goes back to the space the layer was opened in (<see cref="Closing"/>).</summary>
    public void End(IGraphicsDevice gd, float alpha, Rect2D scissor, in LayerMask mask, float soft, Vector4F softCut,
        in LayerMirror mirror, Rect2D mirrorScissor, Vector4F clipBox, Vector4F clipRadii)
    {
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

        // Its bands' textures are given up once it no longer draws them apart - whatever made it draw them so.
        if (entry.BandsFrame != _frame && entry.BandsRing != null)
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
        // Without a soft edge, bands drawn apart are a reflected layer's shadows alone.
        var shadowsOnly = soft <= 0f;
        if (alpha > 0f)
        {
            if (mirror.Shows)
            {
                Composite(gd, open, alpha, mirrorScissor, mask, softTexture, bands, shadowsOnly, mirror, 2f, clipBox,
                    clipRadii);
            }

            Composite(gd, open, alpha, scissor, mask, softTexture, bands, shadowsOnly, mirror,
                mirror.Shows ? 1f : 0f, clipBox, clipRadii);
        }
    }

    // How deep inside what the layer drew each texel lies: its coverage blurred across, then down, into the slot's own
    // texture beside the color, so a reused layer keeps it.
    private void BlurCoverage(IGraphicsDevice gd, OpenOffscreenLayer open, float soft, Vector4F cut)
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

    private void BlurPass(IGraphicsDevice gd, IRenderTarget source, IRenderTarget target, Rect2D area, Vector4F axis,
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

    private static void EnsureSoft(IGraphicsDevice gd, CachedLayer entry)
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

    private static void ReleaseSoft(IGraphicsDevice gd, CachedLayer entry)
    {
        Release(gd, entry.SoftRing);
        Release(gd, new[] { entry.SoftScratch });
        entry.SoftRing = null;
        entry.SoftScratch = null;
        entry.Soft = 0f;
    }

    private static IRenderTarget SoftTarget(IGraphicsDevice gd, CachedLayer entry, Adamantium.Imaging.SurfaceFormat format,
        string name) =>
        gd.CreateRenderTarget(entry.TextureWidth, entry.TextureHeight, MSAALevel.None, format,
            ImageUsageFlagBits.SampledBit, ImageLayout.ShaderReadOnlyOptimal, name);

    private void Composite(IGraphicsDevice gd, OpenOffscreenLayer open, float alpha, Rect2D scissor, in LayerMask mask,
        IRenderTarget soft, IRenderTarget bands, bool shadowsOnly, in LayerMirror mirror, float role, Vector4F clipBox,
        Vector4F clipRadii)
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
        // 0 none apart, 1 all its bands (a soft edge), 2 a reflected layer's shadows alone.
        _composite.BandsOn.SetValue(bands == null ? 0f : shadowsOnly ? 2f : 1f);
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

    private static void EnsureRing(IGraphicsDevice gd, CachedLayer entry, uint width, uint height)
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

    private OffscreenLayerLevel LevelAt(IGraphicsDevice gd, int depth, uint width, uint height) =>
        Store.LevelAt(gd, depth, width, height);

    private static void Release<T>(IGraphicsDevice gd, T[] resources)
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
        foreach (var target in _takenGroupTargets)
        {
            (target.Texture as IDisposable)?.Dispose();
        }

        _takenGroupTargets.Clear();
        _windowStore.DisposeNow();
        foreach (var entry in _cached.Values)
        {
            entry.Store?.DisposeNow();
            (entry.Pending as IDisposable)?.Dispose();
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

        _cached.Clear();
        _composite?.Dispose();
        _composite = null;
        _group?.Dispose();
        _group = null;
        _soft?.Dispose();
        _soft = null;
    }
}
