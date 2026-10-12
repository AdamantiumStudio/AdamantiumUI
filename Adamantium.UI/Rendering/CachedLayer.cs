using Adamantium.Graphics.Core;

namespace Adamantium.UI.Rendering;

internal sealed class CachedLayer
{
    public IRenderTarget[] Ring = [];
    public uint TextureWidth;
    public uint TextureHeight;
    public int Latest = -1;
    public ulong Print;
    public bool Valid;
    public uint Width;
    public uint Height;
    public long LastUsed;
}
