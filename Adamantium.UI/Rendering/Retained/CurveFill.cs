using System;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media.Curves;

namespace Adamantium.UI.Rendering.Retained;

/// <summary>A fill's curves packed for the curve fill shaders (CurveMath.fxh, CurveTiles.fxh): one buffer of words, the
/// coordinates as floats relative to the fill's corner, so a fill far from the origin keeps its precision.</summary>
/// <remarks>Layout, in 32-bit words: a header of <see cref="HeaderWords"/> - the corner and size of the pieces' box (four
/// floats), the piece count, the fill rule (0 non-zero, 1 even-odd), the root tile's side (a float) and where the tiles
/// start - then six floats a piece (start, control, end); then the tiles, <see cref="TileWords"/> each, the root first -
/// its point (two floats), the winding there (signed), where its list starts and how long it is, and where its first
/// child is (0: not cut) - then the lists, piece indexes. See <see cref="CurveTileTree"/>.</remarks>
internal sealed class CurveFill
{
    /// <summary>How many words the header takes.</summary>
    public const int HeaderWords = 8;

    /// <summary>How many words a tile takes.</summary>
    public const int TileWords = 6;

    private CurveFill(uint[] words, Rect bounds, long fingerprint)
    {
        Words = words;
        Bounds = bounds;
        Fingerprint = fingerprint;
    }

    /// <summary>The packed fill.</summary>
    public uint[] Words { get; }

    /// <summary>The box of its pieces, in the geometry's local units.</summary>
    public Rect Bounds { get; }

    /// <summary>What identical fills share: a hash of the words.</summary>
    public long Fingerprint { get; }

    /// <summary>The fill of <paramref name="curves"/>, or null when it encloses nothing.</summary>
    public static CurveFill From(PathCurves curves)
    {
        var tiles = curves.FillTiles;
        if (tiles == null)
        {
            return null;
        }

        var fill = curves.FillPieces;
        var pieces = fill.Pieces;
        var bounds = fill.Bounds;
        var tilesAt = HeaderWords + pieces.Length * 6;
        var entriesAt = tilesAt + tiles.TileCount * TileWords;
        var words = new uint[entriesAt + tiles.Entries.Length];
        words[0] = Word(bounds.X);
        words[1] = Word(bounds.Y);
        words[2] = Word(bounds.Width);
        words[3] = Word(bounds.Height);
        words[4] = (uint)pieces.Length;
        words[5] = curves.FillRule == FillRule.EvenOdd ? 1u : 0u;
        words[6] = Word(tiles.Side);
        words[7] = (uint)tilesAt;

        var origin = new Vector2<double>(bounds.X, bounds.Y);
        for (var i = 0; i < pieces.Length; i++)
        {
            var at = HeaderWords + i * 6;
            Put(words, at, pieces[i].Start - origin);
            Put(words, at + 2, pieces[i].Control - origin);
            Put(words, at + 4, pieces[i].End - origin);
        }

        for (var t = 0; t < tiles.TileCount; t++)
        {
            var at = tilesAt + t * TileWords;
            words[at] = BitConverter.SingleToUInt32Bits(tiles.Points[t * 2]);
            words[at + 1] = BitConverter.SingleToUInt32Bits(tiles.Points[t * 2 + 1]);
            words[at + 2] = unchecked((uint)tiles.Windings[t]);
            words[at + 3] = (uint)(entriesAt + tiles.ListStarts[t]);
            words[at + 4] = (uint)(tiles.ListStarts[t + 1] - tiles.ListStarts[t]);
            words[at + 5] = tiles.Children[t] == 0 ? 0u : (uint)(tilesAt + tiles.Children[t] * TileWords);
        }

        for (var e = 0; e < tiles.Entries.Length; e++)
        {
            words[entriesAt + e] = (uint)tiles.Entries[e];
        }

        return new CurveFill(words, bounds, Hash(words));
    }

    private static uint Word(double value) => BitConverter.SingleToUInt32Bits((float)value);

    private static void Put(uint[] words, int at, Vector2<double> point)
    {
        words[at] = Word(point.X);
        words[at + 1] = Word(point.Y);
    }

    private static long Hash(uint[] words)
    {
        var hash = 0x5A3C_9E1B_7D24_6F81UL;
        foreach (var word in words)
        {
            hash = (hash ^ word) * 0x0000_0100_0000_01B3UL;
        }

        return (long)hash;
    }
}
