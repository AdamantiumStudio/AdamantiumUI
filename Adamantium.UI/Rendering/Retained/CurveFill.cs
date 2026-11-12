using System;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media.Curves;

namespace Adamantium.UI.Rendering.Retained;

/// <summary>A fill's curves packed for the curve fill shaders (CurveMath.fxh, CurveTiles.fxh): one buffer of words, the
/// coordinates as floats relative to the fill's corner, so a fill far from the origin keeps its precision.</summary>
/// <remarks>Layout, in 32-bit words: a header of <see cref="HeaderWords"/> - the corner and size of the pieces' box (four
/// floats), the piece count, the fill rule (0 non-zero, 1 even-odd) - then six floats a piece (start, control, end).
/// </remarks>
internal sealed class CurveFill
{
    /// <summary>How many words the header takes.</summary>
    public const int HeaderWords = 6;

    private CurveFill(uint[] words, Rect bounds, long fingerprint, Vector2<double> extentSum)
    {
        Words = words;
        Bounds = bounds;
        Fingerprint = fingerprint;
        ExtentSum = extentSum;
    }

    /// <summary>How far its pieces reach, summed: the widths of their boxes, and the heights - what bounds how many screen
    /// tiles they can cross at a given scale.</summary>
    public Vector2<double> ExtentSum { get; }

    /// <summary>The packed fill.</summary>
    public uint[] Words { get; }

    /// <summary>The box of its pieces, in the geometry's local units.</summary>
    public Rect Bounds { get; }

    /// <summary>What identical fills share: a hash of the words.</summary>
    public long Fingerprint { get; }

    /// <summary>The fill of <paramref name="curves"/>, or null when it encloses nothing.</summary>
    public static CurveFill From(PathCurves curves)
    {
        var fill = curves.FillPieces;
        var pieces = fill.Pieces;
        var bounds = fill.Bounds;
        if (pieces.Length == 0 || !(bounds.Width > 0) || !(bounds.Height > 0))
        {
            return null;
        }

        var words = new uint[HeaderWords + pieces.Length * 6];
        words[0] = Word(bounds.X);
        words[1] = Word(bounds.Y);
        words[2] = Word(bounds.Width);
        words[3] = Word(bounds.Height);
        words[4] = (uint)pieces.Length;
        words[5] = curves.FillRule == FillRule.EvenOdd ? 1u : 0u;

        var origin = new Vector2<double>(bounds.X, bounds.Y);
        double widths = 0, heights = 0;
        for (var i = 0; i < pieces.Length; i++)
        {
            var at = HeaderWords + i * 6;
            Put(words, at, pieces[i].Start - origin);
            Put(words, at + 2, pieces[i].Control - origin);
            Put(words, at + 4, pieces[i].End - origin);
            // A piece runs one way, so its ends span its box.
            widths += Math.Abs(pieces[i].End.X - pieces[i].Start.X);
            heights += Math.Abs(pieces[i].End.Y - pieces[i].Start.Y);
        }

        return new CurveFill(words, bounds, Hash(words), new Vector2<double>(widths, heights));
    }

    private static uint Word(double value) => BitConverter.SingleToUInt32Bits((float)value);

    private static void Put(uint[] words, int at, Vector2<double> point)
    {
        words[at] = Word(point.X);
        words[at + 1] = Word(point.Y);
    }

    // FNV-1a over the words, with a mark of its own so it never meets a mesh's fingerprint.
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
