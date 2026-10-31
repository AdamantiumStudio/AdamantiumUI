using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Fonts;
using Adamantium.Imaging;
using Adamantium.Vulkan.Core;

namespace Adamantium.UI.Core.Graphics;

public interface IResourceFactory
{
    /// <summary>The widest and tallest 2D texture the device makes (its maxImageDimension2D).</summary>
    uint MaxTextureDimension { get; }

    /// <summary>The most layers a texture array of the device has (its maxImageArrayLayers).</summary>
    uint MaxTextureArrayLayers { get; }

    ITexture CreateTexture(TextureDescription description, byte[] pixelData);

    /// <summary>One texture whose ARRAY LAYERS are <paramref name="layers"/> - an animation's frames, uploaded once and
    /// then selected in the shader by layer, instead of a texture per frame.</summary>
    ITexture CreateTextureArray(TextureDescription description, IReadOnlyList<byte[]> layers);

    /// <summary>Writes new pixels into parts of a texture made here, leaving the rest as it was - atlas cells, map
    /// tiles - on the device that owns the texture.</summary>
    void UpdateTextureRegions(ITexture texture, IReadOnlyList<TextureRegionUpdate> regions) =>
        texture.GraphicsDevice.UpdateTextureRegions(texture, regions);

    /// <summary>A texture array of <paramref name="description"/>, its <c>ArrayLayers</c> the starting capacity, that
    /// grows as layers are taken, up to <paramref name="maxLayers"/> or the device's limit.</summary>
    GrowingTextureArray CreateGrowingTextureArray(TextureDescription description, uint maxLayers) =>
        throw new NotSupportedException($"{GetType().Name} makes no growing texture arrays.");

    /// <summary>Imports an externally produced shared surface (zero-copy) on the UI resource device.</summary>
    ITexture ImportSharedSurface(SharedSurfaceDescriptor descriptor);

    IRenderTarget CreateRenderTarget(UInt32 width,
        UInt32 height,
        MSAALevel msaa,
        SurfaceFormat format,
        ImageLayout desiredLayout);
    
    FontRenderer GetFontRenderer(IGraphicsDevice graphicsDevice);
}