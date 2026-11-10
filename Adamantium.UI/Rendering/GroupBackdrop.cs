using System;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Extensions;
using Adamantium.Imaging;
using Adamantium.Vulkan.Core;

namespace Adamantium.UI.Rendering;

// What lies under a group that blends, copied texel for texel out of the target it is about to be laid down on: the
// blend modes mix each of its colors with the one beneath. A transfer with the pass suspended, like BackdropCapture, but
// at full size and without a pyramid - a blend reads one texel, never a blur.
internal sealed class GroupBackdrop : IDisposable
{
    // One texture per frame in flight: written by a blit and read by a shader in the same frame, a single image would be
    // written by frame N while frame N-1 still samples it. Groups within a frame take turns on one, the barriers ordering
    // each copy after the last read. They only grow, so a frame of groups of every size allocates nothing.
    private Texture[] _ring;
    private uint _width, _height;
    private Format _format;

    /// <summary>The texture the last copy went into: the region at its top-left corner.</summary>
    public Texture Image { get; private set; }

    /// <summary>How much of <see cref="Image"/> the last copy took, as fractions of its size.</summary>
    public (float Width, float Height) Texels { get; private set; }

    /// <summary>Copies <paramref name="region"/> (window device pixels) of the current target. False when there is nothing
    /// to copy; the group then blends with nothing under it.</summary>
    public bool Capture(IGraphicsDevice gd, Rect2D region)
    {
        var source = gd?.CurrentRenderTarget?.ResolveTexture;
        if (source == null || region.Extent.Width == 0 || region.Extent.Height == 0) return false;

        var origin = gd.TargetOrigin;
        var x = region.Offset.X - origin.X;
        var y = region.Offset.Y - origin.Y;
        if (x < 0 || y < 0 || x + region.Extent.Width > source.Width || y + region.Extent.Height > source.Height)
        {
            return false;
        }

        // In the target's own format: a blend-mode group rewrites its whole region from this copy, so a copy that lost
        // anything - sRGB decoded to eight bits, ten bits cut to eight - would band what the group does not even touch.
        var format = source is Texture texture ? texture.Description.Format : Format.R8G8B8A8_UNORM;
        Ensure(gd, region.Extent.Width, region.Extent.Height, format);
        Image = _ring[gd.CurrentFrame % (uint)_ring.Length];
        Texels = (region.Extent.Width / (float)_width, region.Extent.Height / (float)_height);

        var commandBuffer = gd.CurrentCommandBuffer;
        gd.SuspendRendering();
        source.ImageLayout = ImageLayout.TransferSrcOptimal;
        Image.ImageLayout = ImageLayout.TransferDstOptimal;
        gd.InsertImageMemoryBarrier(commandBuffer, source,
            AccessFlagBits2.ColorAttachmentWriteBit, AccessFlagBits2.TransferReadBit,
            ImageLayout.ColorAttachmentOptimal, ImageLayout.TransferSrcOptimal,
            PipelineStageFlagBits2.ColorAttachmentOutputBit, PipelineStageFlagBits2.AllTransferBit);
        gd.InsertImageMemoryBarrier(commandBuffer, Image,
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
                new Offset3D { X = x + (int)region.Extent.Width, Y = y + (int)region.Extent.Height, Z = 1 }
            },
            DstOffsets = new[]
            {
                new Offset3D { X = 0, Y = 0, Z = 0 },
                new Offset3D { X = (int)region.Extent.Width, Y = (int)region.Extent.Height, Z = 1 }
            }
        };
        commandBuffer.BlitImage(source.GetImage(), ImageLayout.TransferSrcOptimal,
            Image.GetImage(), ImageLayout.TransferDstOptimal, 1, blit, Filter.Nearest);

        gd.InsertImageMemoryBarrier(commandBuffer, Image,
            AccessFlagBits2.TransferWriteBit, AccessFlagBits2.ShaderReadBit,
            ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal,
            PipelineStageFlagBits2.AllTransferBit, PipelineStageFlagBits2.FragmentShaderBit);
        Image.ImageLayout = ImageLayout.ShaderReadOnlyOptimal;
        gd.InsertImageMemoryBarrier(commandBuffer, source,
            AccessFlagBits2.TransferReadBit, AccessFlagBits2.ColorAttachmentWriteBit,
            ImageLayout.TransferSrcOptimal, ImageLayout.ColorAttachmentOptimal,
            PipelineStageFlagBits2.AllTransferBit, PipelineStageFlagBits2.ColorAttachmentOutputBit);
        source.ImageLayout = ImageLayout.ColorAttachmentOptimal;
        gd.ResumeRendering();
        return true;
    }

    private void Ensure(IGraphicsDevice device, uint width, uint height, Format format)
    {
        _ring ??= new Texture[Math.Max(1, (int)device.MaxFramesInFlight)];
        if (_ring[0] != null && _width >= width && _height >= height && _format == format) return;

        _format = format;

        foreach (var old in _ring)
        {
            if (old != null) device.AddToDeferDisposeQueue(old);
        }

        _width = Math.Max(_width, width);
        _height = Math.Max(_height, height);
        for (var i = 0; i < _ring.Length; i++)
        {
            _ring[i] = Graphics.Texture.New(device, new TextureDescription
            {
                Width = _width,
                Height = _height,
                Depth = 1,
                ArrayLayers = 1,
                MipLevels = 1,
                Samples = MSAALevel.None,
                Format = format,
                InitialLayout = ImageLayout.Undefined,
                DesiredImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                ImageType = ImageType._2d,
                ImageAspect = ImageAspectFlagBits.ColorBit,
                ImageTiling = Vulkan.Core.ImageTiling.Optimal,
                Usage = ImageUsageFlagBits.SampledBit | ImageUsageFlagBits.TransferDstBit,
                Dimension = TextureDimension.Texture2D
            }, $"GroupBackdrop:{i}");
        }
    }

    public void Dispose()
    {
        if (_ring == null) return;

        for (var i = 0; i < _ring.Length; i++)
        {
            _ring[i]?.Dispose();
            _ring[i] = null;
        }

        Image = null;
    }
}
