using System;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media.Curves;

namespace Adamantium.UI.Rendering.Retained;

/// <summary>A fill's curves packed for the curve fill shader (BatchEffect.fx, pass CurveFill): one buffer of words, the
/// coordinates as floats relative to the fill's corner, so a fill far from the origin keeps its precision.</summary>
/// <remarks>Layout, in 32-bit words: a header of <see cref="HeaderWords"/> - the corner and size of the banded box (four
/// floats), the band count, the piece count, the fill rule (0 non-zero, 1 even-odd), then the offsets of the row starts,
/// the rows, the column starts and the columns - then six floats a piece (start, control, end), then the four lists.
/// </remarks>
internal sealed class CurveFill
{
    /// <summary>How many words the header takes.</summary>
    public const int HeaderWords = 12;

    private CurveFill(uint[] words, Rect bounds, long fingerprint)
    {
        Words = words;
        Bounds = bounds;
        Fingerprint = fingerprint;
    }

    /// <summary>The packed fill.</summary>
    public uint[] Words { get; }

    /// <summary>The box its pieces are banded in, in the geometry's local units.</summary>
    public Rect Bounds { get; }

    /// <summary>What identical fills share: a hash of the words.</summary>
    public long Fingerprint { get; }

    /// <summary>The fill of <paramref name="curves"/>, or null when it encloses nothing.</summary>
    public static CurveFill From(PathCurves curves)
    {
        var bands = curves.FillBands;
        if (bands.Pieces.Length == 0)
        {
            return null;
        }

        var bounds = bands.Bounds;
        if (!(bounds.Width > 0) || !(bounds.Height > 0))
        {
            return null;
        }

        var pieces = bands.Pieces;
        var count = bands.BandCount;
        var rowStarts = HeaderWords + pieces.Length * 6;
        var rows = rowStarts + count + 1;
        var columnStarts = rows + bands.Rows.Length;
        var columns = columnStarts + count + 1;
        var words = new uint[columns + bands.Columns.Length];

        words[0] = Word(bounds.X);
        words[1] = Word(bounds.Y);
        words[2] = Word(bounds.Width);
        words[3] = Word(bounds.Height);
        words[4] = (uint)count;
        words[5] = (uint)pieces.Length;
        words[6] = curves.FillRule == FillRule.EvenOdd ? 1u : 0u;
        words[7] = (uint)rowStarts;
        words[8] = (uint)rows;
        words[9] = (uint)columnStarts;
        words[10] = (uint)columns;

        var origin = new Vector2<double>(bounds.X, bounds.Y);
        for (var i = 0; i < pieces.Length; i++)
        {
            var at = HeaderWords + i * 6;
            Put(words, at, pieces[i].Start - origin);
            Put(words, at + 2, pieces[i].Control - origin);
            Put(words, at + 4, pieces[i].End - origin);
        }

        Copy(bands.RowStarts, words, rowStarts);
        Copy(bands.Rows, words, rows);
        Copy(bands.ColumnStarts, words, columnStarts);
        Copy(bands.Columns, words, columns);
        return new CurveFill(words, bounds, Hash(words));
    }

    private static uint Word(double value) => BitConverter.SingleToUInt32Bits((float)value);

    private static void Put(uint[] words, int at, Vector2<double> point)
    {
        words[at] = Word(point.X);
        words[at + 1] = Word(point.Y);
    }

    private static void Copy(int[] source, uint[] words, int at)
    {
        for (var i = 0; i < source.Length; i++)
        {
            words[at + i] = (uint)source[i];
        }
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
