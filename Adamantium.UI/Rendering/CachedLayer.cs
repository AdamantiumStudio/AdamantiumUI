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

    // Soft edges: a slot's blurred coverage beside its color, the pass between, and the width the latest was blurred at.
    public IRenderTarget[] SoftRing;
    public IRenderTarget SoftScratch;
    public uint SoftWidth;
    public uint SoftHeight;
    public float Soft;

    // A soft-edged layer's own bands, drawn apart from what the fade reaches: the slot drawn last, and in which frame.
    public IRenderTarget[] BandsRing;
    public uint BandsWidth;
    public uint BandsHeight;
    public int BandsLatest = -1;
    public long BandsFrame = -1;
}
