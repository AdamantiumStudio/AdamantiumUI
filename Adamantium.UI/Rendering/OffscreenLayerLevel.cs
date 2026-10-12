using Adamantium.Graphics.Core;

namespace Adamantium.UI.Rendering;

internal sealed class OffscreenLayerLevel
{
    public IDepthStencilBuffer[] Depth = [];
    public uint Width;
    public uint Height;
}
