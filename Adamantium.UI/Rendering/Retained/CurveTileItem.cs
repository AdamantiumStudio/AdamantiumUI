using System.Runtime.InteropServices;
using Adamantium.Mathematics;

namespace Adamantium.UI.Rendering.Retained;

/// <summary>One curve fill instance of a flush as the tile binning reads it - see CurveTiles.fxh, whose struct this
/// matches field for field.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct CurveTileItem
{
    /// <summary>The fill's local space to framebuffer pixels, the x row: x = dot((p, 1), ToPixelX.xyz).</summary>
    public Vector4F ToPixelX;

    /// <summary>The y row.</summary>
    public Vector4F ToPixelY;

    /// <summary>Device address of the fill's packed curves.</summary>
    public ulong Curves;

    /// <summary>Its first piece among all the flush's.</summary>
    public uint PieceFirst;

    public uint PieceCount;

    /// <summary>Its tile grid's corner, in tiles from the framebuffer's.</summary>
    public int TileX;

    public int TileY;
    public uint Columns;
    public uint Rows;

    /// <summary>Its first tile in the tile arrays.</summary>
    public uint TileBase;

    /// <summary>Its first tile row among all the flush's.</summary>
    public uint RowBase;

    public uint EvenOdd;
    public uint Pad;
}
