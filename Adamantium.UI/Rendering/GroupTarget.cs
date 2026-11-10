using Adamantium.Graphics.Core;
using Adamantium.Vulkan.Core;

namespace Adamantium.UI.Rendering;

/// <summary>What a group of draws, or one of its masks, was drawn into this frame: the texture, the window region it
/// holds at its top-left corner, and the texture's size.</summary>
internal readonly record struct GroupTarget(IRenderTarget Texture, Rect2D Region, uint TextureWidth, uint TextureHeight)
{
    public bool Drawn => Texture != null;

    /// <summary>How much of the texture the region takes, as fractions of its size.</summary>
    public (float Width, float Height) Texels => (Region.Extent.Width / (float)TextureWidth,
        Region.Extent.Height / (float)TextureHeight);
}
