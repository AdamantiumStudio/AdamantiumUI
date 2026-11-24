using Adamantium.Graphics.Core;

namespace Adamantium.UI.Rendering;

internal sealed class OffscreenLayerLevel
{
    public IDepthStencilBuffer[] Depth = [];
    public uint Width;
    public uint Height;

    // The largest asked of it since AskedSince: what it is made smaller to when it holds far more.
    public uint AskedWidth;
    public uint AskedHeight;
    public long AskedSince;
}
