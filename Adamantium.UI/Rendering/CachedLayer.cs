using Adamantium.Graphics.Core;

namespace Adamantium.UI.Rendering;

internal sealed class CachedLayer
{
    public IRenderTarget[] Ring = [];

    // A raster's texture: the targets and depth what is drawn into it takes, kept from one drawing of it to the next.
    public OffscreenTargetStore Store;

    // A raster's texture being drawn anew, square by square, beside the one laid down: its size, what it will hold, and
    // whether a square has gone in yet (the first clears it).
    public IRenderTarget Pending;
    public uint PendingWidth;
    public uint PendingHeight;
    public ulong PendingPrint;
    public bool PendingCleared;
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
