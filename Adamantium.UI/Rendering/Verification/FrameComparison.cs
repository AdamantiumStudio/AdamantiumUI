using System;

namespace Adamantium.UI.Rendering.Verification;

internal sealed class FrameComparison
{
    private FrameComparison(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public int Width { get; }

    public int Height { get; }

    public int Different { get; private set; }

    public int Extra { get; private set; }

    public int Missing { get; private set; }

    public int Masked { get; private set; }

    public int MaxDelta { get; private set; }

    public int Left { get; private set; } = int.MaxValue;

    public int Top { get; private set; } = int.MaxValue;

    public int Right { get; private set; } = -1;

    public int Bottom { get; private set; } = -1;

    public bool[] DifferentAt { get; private set; }

    public bool HasDifference => Different > 0;

    /// <summary>Pixels are four bytes; <paramref name="clear"/> is the background in the same byte order, so a pixel equal to
    /// it is "nothing drawn".</summary>
    public static FrameComparison Compare(byte[] live, byte[] walk, int width, int height, bool[] masked, uint clear)
    {
        var result = new FrameComparison(width, height) { DifferentAt = new bool[width * height] };
        var count = Math.Min(Math.Min(live.Length, walk.Length) / 4, width * height);

        for (var i = 0; i < count; i++)
        {
            var at = i * 4;
            var a = BitConverter.ToUInt32(live, at);
            var b = BitConverter.ToUInt32(walk, at);
            if (a == b)
            {
                continue;
            }

            if (masked != null && masked[i])
            {
                result.Masked++;
                continue;
            }

            result.DifferentAt[i] = true;
            result.Different++;

            if (b == clear)
            {
                result.Extra++;
            }
            else if (a == clear)
            {
                result.Missing++;
            }

            for (var c = 0; c < 4; c++)
            {
                result.MaxDelta = Math.Max(result.MaxDelta, Math.Abs(live[at + c] - walk[at + c]));
            }

            var x = i % width;
            var y = i / width;
            result.Left = Math.Min(result.Left, x);
            result.Top = Math.Min(result.Top, y);
            result.Right = Math.Max(result.Right, x);
            result.Bottom = Math.Max(result.Bottom, y);
        }

        return result;
    }
}
