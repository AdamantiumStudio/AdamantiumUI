using System;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Extensions;
using Adamantium.Mathematics;
using Adamantium.Imaging;
using Adamantium.Vulkan.Core;

namespace Adamantium.UI.Rendering;

// Copies the already-drawn frame region behind an element for backdrop materials: a transfer out of the color target
// (the pass is suspended), not a re-render. The region is grown by a margin so blurs do not darken at the edge.
internal sealed class BackdropCapture : IDisposable
{
    // Downscale for blurring materials, where the filtered shrink is the first blur pass; refracting glass samples a
    // sharp copy instead (see Sharp).
    public const int Downscale = 4;

    /// <summary>What a material that BENDS the copy asks for instead: no shrink at all. The copy is of the element's own
    /// region plus a margin - a menu or a panel, not the window - so a full-resolution blit of it is one small transfer,
    /// and it is the only way the lens has anything to bend.</summary>
    public const int Sharp = 1;

    // ONE TEXTURE PER FRAME IN FLIGHT, not one texture. The capture is written by a blit and read by a shader in the
    // SAME frame, so a single image is written by frame N while frame N-1 is still sampling it - a write-after-read the
    // barriers inside one command buffer say nothing about, and the way this shows up is the GPU dying with the
    // validation layer silent. Indexed by the device's current frame, exactly as ReusableBuffer's ring is.
    private Texture[] _ring;
    private Texture _current;
    private uint _width, _height;

    /// <summary>The last captured image, or null if nothing has been captured yet. Bound by the material pass.</summary>
    public ITexture Image => _current;

    /// <summary>Where the capture came from, in DEVICE pixels - the material's pixel shader needs it to map a fragment
    /// back into the copy.</summary>
    public Rect2D Region { get; private set; }

    /// <summary>Copy the frame region behind an element into this capture, breaking the render pass open around the
    /// transfer and re-opening it afterwards. Returns false when there is nothing to copy (no target, empty region) -
    /// the caller then draws the element without a backdrop rather than with a stale one.</summary>
    public bool Capture(IGraphicsDevice device, Rect2D region, int downscale = Downscale)
    {
        if (device is not GraphicsDevice gd) return false;

        downscale = Math.Max(1, downscale);

        var source = gd.CurrentRenderTarget?.ResolveTexture;
        if (source == null) return false;

        // Clamp to the target: a region grown by the blur margin can hang off the edge, and a blit past the source
        // bounds is a GPU fault, not a clipped copy.
        var x = (int)Math.Clamp(region.Offset.X, 0, (int)source.Width);
        var y = (int)Math.Clamp(region.Offset.Y, 0, (int)source.Height);
        var right = (int)Math.Clamp(region.Offset.X + region.Extent.Width, 0, source.Width);
        var bottom = (int)Math.Clamp(region.Offset.Y + region.Extent.Height, 0, source.Height);
        if (right - x < downscale || bottom - y < downscale) return false;

        var w = (uint)Math.Max(1, (right - x) / downscale);
        var h = (uint)Math.Max(1, (bottom - y) / downscale);

        // The copy size rounds down to a multiple of 2^halvings so pyramid levels halve exactly; the region stays as is,
        // since the blit scales it to the copy.
        var align = 1u << Halvings(w, h);
        w -= w % align;
        h -= h % align;
        EnsureTexture(gd, w, h);
        _current = _ring[gd.CurrentFrame % (uint)_ring.Length];
        if (_current == null) return false;

        Region = new Rect2D
        {
            Offset = new Offset2D { X = x, Y = y },
            Extent = new Extent2D { Width = (uint)(right - x), Height = (uint)(bottom - y) }
        };

        var commandBuffer = gd.CurrentCommandBuffer;
        gd.SuspendRendering();

        // Syncs the texture objects' tracked layouts with the barriers below; assigned, because TransitionImageLayout
        // opens its own command buffer, which crashes mid-recording.
        source.ImageLayout = ImageLayout.TransferSrcOptimal;
        _current.ImageLayout = ImageLayout.TransferDstOptimal;

        gd.InsertImageMemoryBarrier(commandBuffer, source,
            AccessFlagBits2.ColorAttachmentWriteBit, AccessFlagBits2.TransferReadBit,
            ImageLayout.ColorAttachmentOptimal, ImageLayout.TransferSrcOptimal,
            PipelineStageFlagBits2.ColorAttachmentOutputBit, PipelineStageFlagBits2.AllTransferBit);

        gd.InsertImageMemoryBarrier(commandBuffer, _current,
            AccessFlagBits2.ShaderReadBit, AccessFlagBits2.TransferWriteBit,
            ImageLayout.ShaderReadOnlyOptimal, ImageLayout.TransferDstOptimal,
            PipelineStageFlagBits2.FragmentShaderBit, PipelineStageFlagBits2.AllTransferBit);

        var blit = new ImageBlit
        {
            SrcSubresource = new ImageSubresourceLayers { AspectMask = ImageAspectFlagBits.ColorBit, LayerCount = 1 },
            DstSubresource = new ImageSubresourceLayers { AspectMask = ImageAspectFlagBits.ColorBit, LayerCount = 1 },
            SrcOffsets = new[]
            {
                new Offset3D { X = x, Y = y, Z = 0 },
                new Offset3D { X = right, Y = bottom, Z = 1 }
            },
            DstOffsets = new[]
            {
                new Offset3D { X = 0, Y = 0, Z = 0 },
                new Offset3D { X = (int)w, Y = (int)h, Z = 1 }
            }
        };

        // Linear, not Nearest: the filtering IS the first blur pass (see the note above).
        commandBuffer.BlitImage(source.GetImage(), ImageLayout.TransferSrcOptimal,
            _current.GetImage(), ImageLayout.TransferDstOptimal, 1, blit, Filter.Linear);

        // Leaves EVERY level, level 0 included, in ShaderReadOnly - so nothing more is owed here.
        BuildPyramid(gd, commandBuffer, _current, (int)w, (int)h);

        gd.InsertImageMemoryBarrier(commandBuffer, source,
            AccessFlagBits2.TransferReadBit, AccessFlagBits2.ColorAttachmentWriteBit,
            ImageLayout.TransferSrcOptimal, ImageLayout.ColorAttachmentOptimal,
            PipelineStageFlagBits2.AllTransferBit, PipelineStageFlagBits2.ColorAttachmentOutputBit);

        source.ImageLayout = ImageLayout.ColorAttachmentOptimal;
        _current.ImageLayout = ImageLayout.ShaderReadOnlyOptimal;

        gd.ResumeRendering();
        return true;
    }

    /// <summary>The smallest a level is allowed to get. Below this a level stops being a blur of the picture and
    /// becomes an average of the whole thing.</summary>
    private const uint SmallestLevel = 8;

    /// <summary>The most halvings a copy will carry: level 6 is a blur 64 device pixels wide, past anything a surface
    /// asks for.</summary>
    private const int MaxHalvings = 6;

    // How many exact halvings the size allows, which the copy size must be a multiple of; a taller pyramid keeps large
    // radii from clamping on high-DPI displays.
    private static int Halvings(uint width, uint height)
    {
        var smaller = Math.Min(width, height);
        var halvings = 0;
        while (halvings < MaxHalvings && (smaller >> (halvings + 1)) >= SmallestLevel) halvings++;
        return halvings;
    }

    // Levels stop where halving stops being exact; an odd size would shift UVs between levels and make the backdrop
    // slide as the blur widens.
    internal static uint CountLevels(uint width, uint height)
    {
        var levels = 1u;
        while (width > 1 && height > 1 && (width & 1) == 0 && (height & 1) == 0)
        {
            width >>= 1;
            height >>= 1;
            levels++;
        }

        return levels;
    }

    // Each level is drawn from the one above with a 13-tap filter (CaptureBlurEffect), since a linear blit aliases into
    // moire. Barriers are per level: each written level becomes the next source.
    private void BuildPyramid(GraphicsDevice gd, CommandBuffer commandBuffer, Texture texture, int width, int height)
    {
        var levels = CountLevels((uint)width, (uint)height);
        if (levels < 2) return;

        _blur ??= new Adamantium.UI.FX.CaptureBlurEffect(gd);

        // THE VIEWPORT AND SCISSOR ARE THE FRAME'S, and they are CACHED - the device sends them only when they change.
        // Each level here needs its own, tiny, and leaving the last one behind clipped everything the frame drew after
        // the material away: the pane appeared and the panel beside it did not.
        var viewport = gd.CurrentViewports.Length > 0 ? gd.CurrentViewports[0] : default;
        var scissor = gd.CurrentScissors.Length > 0 ? gd.CurrentScissors[0] : default;

        var w = width;
        var h = height;

        // Level 0 arrives as a transfer destination; it is about to be READ.
        gd.InsertImageMemoryBarrier(commandBuffer, texture,
            AccessFlagBits2.TransferWriteBit, AccessFlagBits2.ShaderReadBit,
            ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal,
            PipelineStageFlagBits2.AllTransferBit, PipelineStageFlagBits2.FragmentShaderBit,
            0, 1);

        for (var level = 1u; level < levels; level++)
        {
            var nw = Math.Max(1, w / 2);
            var nh = Math.Max(1, h / 2);

            gd.InsertImageMemoryBarrier(commandBuffer, texture,
                AccessFlagBits2.None, AccessFlagBits2.ColorAttachmentWriteBit,
                ImageLayout.Undefined, ImageLayout.ColorAttachmentOptimal,
                PipelineStageFlagBits2.TopOfPipeBit, PipelineStageFlagBits2.ColorAttachmentOutputBit,
                level, 1);

            var attachment = new RenderingAttachmentInfo
            {
                ImageView = texture.GetLevelView(level),
                ImageLayout = ImageLayout.ColorAttachmentOptimal,
                LoadOp = AttachmentLoadOp.DontCare,
                StoreOp = AttachmentStoreOp.Store
            };

            var info = new RenderingInfo
            {
                RenderArea = new Rect2D
                {
                    Offset = new Offset2D(),
                    Extent = new Extent2D { Width = (uint)nw, Height = (uint)nh }
                },
                PColorAttachments = new[] { attachment },
                ColorAttachmentCount = 1,
                LayerCount = 1
            };

            commandBuffer.BeginRendering(info);
            gd.SetViewports(new Viewport { Width = nw, Height = nh, MaxDepth = 1.0f });
            gd.SetScissors(info.RenderArea);

            _blur.SourceTexture.SetResource(texture);
            _blur.SourceSampler.SetResource(SamplerStates.LinearClampToEdge);
            _blur.BlurStep.SetValue(new Vector4F(level - 1, nw, nh, 0));
            _blur.CaptureBlurDownPass.Apply();
            gd.Draw(3, 1);

            commandBuffer.EndRendering();

            // What was just drawn becomes the next step's source.
            gd.InsertImageMemoryBarrier(commandBuffer, texture,
                AccessFlagBits2.ColorAttachmentWriteBit, AccessFlagBits2.ShaderReadBit,
                ImageLayout.ColorAttachmentOptimal, ImageLayout.ShaderReadOnlyOptimal,
                PipelineStageFlagBits2.ColorAttachmentOutputBit, PipelineStageFlagBits2.FragmentShaderBit,
                level, 1);

            w = nw;
            h = nh;
        }

        gd.SetViewports(viewport);
        gd.SetScissors(scissor);
    }

    private Adamantium.UI.FX.CaptureBlurEffect _blur;


    // Re-made only on size change; old images go to the deferred queue, since in-flight frames may still sample them.
    private void EnsureTexture(GraphicsDevice device, uint width, uint height)
    {
        _ring ??= new Texture[Math.Max(1, (int)device.MaxFramesInFlight)];
        if (_ring[0] != null && _width == width && _height == height) return;

        for (var i = 0; i < _ring.Length; i++)
        {
            if (_ring[i] != null) device.AddToDeferDisposeQueue(_ring[i]);
            _ring[i] = null;
        }

        _current = null;
        _width = width;
        _height = height;
        for (var i = 0; i < _ring.Length; i++)
        {
            _ring[i] = Graphics.Texture.New(device, new TextureDescription
            {
                Width = width,
                Height = height,
                Depth = 1,
                ArrayLayers = 1,
                // A PYRAMID, because that is how a wide blur is actually built: not one wide kernel over the full-size
                // copy, but a small one over a smaller image. Each level halves both axes, so level N is a blur of
                // radius 2^N for the price of a single sample - and there is no radius at which it falls apart.
                MipLevels = CountLevels(width, height),
                Samples = MSAALevel.None,
                Format = Format.R8G8B8A8_UNORM,
                InitialLayout = ImageLayout.Undefined,
                DesiredImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                ImageType = ImageType._2d,
                ImageAspect = ImageAspectFlagBits.ColorBit,
                ImageTiling = Vulkan.Core.ImageTiling.Optimal,
                // TransferSrc as well: every level but the last is the SOURCE of the blit that makes the next one.
                // ColorAttachment because a level is also DRAWN into - the filter that fills it is a shader, and a
                // shader writes through an attachment. No descriptor is involved in that direction, which is why this
                // route needs nothing from the heap.
                Usage = ImageUsageFlagBits.SampledBit | ImageUsageFlagBits.TransferDstBit
                        | ImageUsageFlagBits.TransferSrcBit | ImageUsageFlagBits.ColorAttachmentBit,
                Dimension = TextureDimension.Texture2D
            }, $"BackdropCapture:{i}");
        }
    }

    public void Dispose()
    {
        _blur?.Dispose();
        _blur = null;
        if (_ring == null) return;

        for (var i = 0; i < _ring.Length; i++)
        {
            _ring[i]?.Dispose();
            _ring[i] = null;
        }

        _current = null;
    }
}
