using System;
using System.Collections.Generic;

namespace Adamantium.UI.Core.Text;

/// <summary>A stretch of a text: where it starts and how many UTF-16 characters it holds.</summary>
public readonly record struct TextRange(int Start, int Length)
{
    public int End => Start + Length;

    /// <summary>The <paramref name="ranges"/> of <paramref name="before"/> moved with the edit that made it
    /// <paramref name="after"/>: those ahead of the stretch edited stay, those past it move with the text, those it
    /// touched are gone. The same list when the text is the same.</summary>
    public static IReadOnlyList<TextRange> Follow(IReadOnlyList<TextRange> ranges, string before, string after)
    {
        if (before == after)
        {
            return ranges;
        }

        var head = 0;
        var most = Math.Min(before.Length, after.Length);
        while (head < most && before[head] == after[head])
        {
            head++;
        }

        var tail = 0;
        while (tail < most - head && before[before.Length - 1 - tail] == after[after.Length - 1 - tail])
        {
            tail++;
        }

        var changedEnd = before.Length - tail;
        var shift = after.Length - before.Length;
        List<TextRange> moved = [];
        foreach (var range in ranges)
        {
            if (range.Start < 0 || range.End > before.Length)
            {
                continue;
            }

            if (range.End < head)
            {
                moved.Add(range);
            }
            else if (range.Start > changedEnd)
            {
                moved.Add(new TextRange(range.Start + shift, range.Length));
            }
        }

        return moved;
    }
}
