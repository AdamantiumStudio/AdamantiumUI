using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Graphics.Vector;

/// <summary>A glyph of laid-out text as a vector writer sets it: its pen position on the baseline, y down.</summary>
/// <param name="Text">What it stands for; empty for nothing, null for what its font says.</param>
/// <param name="IsUpright">Whether it stands as its font has it, neither turned nor stretched.</param>
internal readonly record struct TextGlyph(IFont Font, double Size, uint Index, double X, double Y, string Text,
    bool IsUpright)
{
    private const int SoftHyphen = 0x00AD;
    private const int Kashida = 0x0640;
    private const int ZeroWidthSpace = 0x200B;
    private const int LineSeparator = 0x2028;
    private const int ParagraphSeparator = 0x2029;

    /// <summary>The glyphs of <paramref name="layout"/> that draw something, in the order it has them. With
    /// <paramref name="said"/> its spaces too, and what each stands for: the first glyph of a cluster for all its
    /// characters, the rest for none.</summary>
    public static List<TextGlyph> Of(TextRenderingParameters parameters, TextLayout layout, bool said)
    {
        var glyphs = new List<TextGlyph>();
        var area = parameters?.TextArea ?? default;
        var words = layout.GetTextData();
        var ends = said ? Ends(words) : null;
        var first = new HashSet<int>();
        foreach (var word in words)
        {
            var glyph = word.Glyph;
            if (glyph == null || word.Font == null || !(word.FontSize > 0) || word.Symbol is '\n' or '\r')
            {
                continue;
            }

            var isBlank = word.Rect.Width <= 0 || !glyph.HasOutlines;
            if (isBlank)
            {
                if (!said || !char.IsWhiteSpace(word.Symbol))
                {
                    continue;
                }

                // A tab has the space of the layout's own font, whichever its run is in.
                glyph = word.Font.GetGlyphByCharacter(' ');
                if (glyph == null || glyph.Index == 0 || glyph.HasOutlines)
                {
                    continue;
                }
            }

            string text = null;
            if (said && !word.Placed && !word.Annotates)
            {
                text = word.PositionInString >= 0
                    ? first.Add(word.PositionInString) ? Said(layout.Text, word, ends[word.PositionInString]) : string.Empty
                    : (int)word.Symbol switch
                    {
                        '-' => ((char)SoftHyphen).ToString(),   // a hyphen a line is broken at
                        Kashida => string.Empty,                // a letter drawn out
                        _ => null,
                    };
            }

            // A blank's box stands on the baseline at the pen.
            var box = isBlank ? default : glyph.BoundingRectangle;
            var scale = word.FontSize / word.Font.UnitsPerEm;
            glyphs.Add(new TextGlyph(word.Font, word.FontSize, glyph.Index, word.Rect.X + area.X - box.X * scale,
                word.Rect.Y + area.Y + (box.Y + box.Height) * scale, text,
                !word.Sideways && !word.Upright && word.HorizontalScale == 1));
        }

        return glyphs;
    }

    // Where the characters of each cluster end - where the next one of its line starts; -1 for the last of a line, past
    // which there may be what was cut off it.
    private static Dictionary<int, int> Ends(GlyphWordData[] words)
    {
        var lines = new SortedDictionary<int, int>();
        foreach (var word in words)
        {
            if (word.PositionInString >= 0)
            {
                lines.TryAdd(word.PositionInString, word.LineIndex);
            }
        }

        var ends = new Dictionary<int, int>(lines.Count);
        var previous = -1;
        var line = 0;
        foreach (var (start, at) in lines)
        {
            if (previous >= 0)
            {
                ends[previous] = at == line ? start : -1;
            }

            (previous, line) = (start, at);
        }

        if (previous >= 0)
        {
            ends[previous] = -1;
        }

        return ends;
    }

    // As far as the glyph itself reaches: the longest its font says it stands for, or one character as a reader sees it.
    private static int Reach(string text, GlyphWordData word)
    {
        var start = word.PositionInString;
        var length = StringInfo.GetNextTextElementLength(text, start);
        foreach (var own in word.Font.GetGlyphText(word.Glyph.Index))
        {
            if (own.Length > length && string.CompareOrdinal(text, start, own, 0, own.Length) == 0)
            {
                length = own.Length;
            }
        }

        return start + length;
    }

    // Breaks of lines and the hints where one may be are not text.
    private static string Said(string text, GlyphWordData word, int end)
    {
        if (text == null || word.PositionInString >= text.Length)
        {
            return string.Empty;
        }

        var said = new StringBuilder();
        end = Math.Min(end < 0 ? Reach(text, word) : end, text.Length);
        for (var at = word.PositionInString; at < end; at++)
        {
            if (text[at] == '\t')
            {
                said.Append(' ');
            }
            else if ((int)text[at] is SoftHyphen or ZeroWidthSpace or LineSeparator or ParagraphSeparator)
            {
                continue;
            }
            else if (!char.IsControl(text[at]))
            {
                said.Append(text[at]);
            }
        }

        return said.ToString();
    }
}
