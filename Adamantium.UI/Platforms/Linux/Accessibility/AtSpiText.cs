using System;
using System.Collections.Generic;
using System.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Platforms.Linux.Accessibility;

/// <summary>The text of an element as AT-SPI reads it: from its text pattern where it has one - with selection,
/// lines and bounds - else from its value, as a text box that only says what it holds. Offsets are AT-SPI's
/// characters - code points - not the UTF-16 units of the string.</summary>
internal sealed class AtSpiText
{
    private readonly ITextProvider _text;
    private int? _length;

    private AtSpiText(ITextProvider text, IValueProvider value)
    {
        _text = text;
        Content = text?.Text ?? value?.Value ?? string.Empty;
    }

    /// <summary>The text of <paramref name="peer"/>, or null where it shows none to read.</summary>
    public static AtSpiText Of(AutomationPeer peer) => peer switch
    {
        null or { IsPassword: true } => null,
        _ when peer.GetPattern(PatternId.Text) is ITextProvider text => new AtSpiText(text, peer.GetPattern(PatternId.Value) as IValueProvider),
        _ when peer.GetPattern(PatternId.Value) is IValueProvider value => new AtSpiText(null, value),
        _ => null,
    };

    public string Content { get; }

    /// <summary>How many characters the text holds.</summary>
    public int Length => _length ??= Characters(Content, Content.Length);

    public bool IsMultiline => _text?.IsMultiline ?? false;

    public int SelectionStart => _text == null ? Length : Characters(Content, _text.SelectionStart);

    public int SelectionEnd => _text == null ? Length : Characters(Content, _text.SelectionStart + _text.SelectionLength);

    public int Caret => _text == null ? Length : Characters(Content, _text.CaretIndex);

    public bool CanSelect => _text != null;

    /// <summary>Selects from <paramref name="anchor"/> to <paramref name="caret"/>, the caret at the second.</summary>
    public void Select(int anchor, int caret)
    {
        var start = Units(Content, anchor);
        _text?.Select(start, Units(Content, caret) - start);
    }

    public string Substring(int start, int end)
    {
        var from = Units(Content, start);
        return Content[from..Math.Max(from, Units(Content, end))];
    }

    /// <summary>The code point at <paramref name="offset"/>; 0 past the end.</summary>
    public int CharacterAt(int offset)
    {
        var unit = Units(Content, offset);
        if (offset < 0 || unit >= Content.Length)
        {
            return 0;
        }

        Rune.DecodeFromUtf16(Content.AsSpan(unit), out var rune, out _);
        return rune.Value;
    }

    /// <summary>The characters of the line <paramref name="offset"/> is on.</summary>
    public (int Start, int End) LineAt(int offset)
    {
        if (_text == null)
        {
            return (0, Length);
        }

        var (start, end) = _text.LineRange(_text.LineOf(Units(Content, offset)));
        return (Characters(Content, start), Characters(Content, end));
    }

    public IReadOnlyList<Rect> Bounds(int start, int end) => _text?.Bounds(Units(Content, start), Units(Content, end)) ?? [];

    public int IndexAt(PixelPoint point) => _text == null ? -1 : Characters(Content, _text.IndexAt(point));

    public void ScrollTo(int offset) => _text?.ScrollIntoView(Units(Content, offset));

    /// <summary>The UTF-16 index of the character <paramref name="offset"/> of <paramref name="text"/>, within it.</summary>
    public static int Units(string text, int offset)
    {
        var unit = 0;
        for (var character = 0; character < offset && unit < text.Length; character++)
        {
            unit += char.IsSurrogatePair(text, unit) ? 2 : 1;
        }

        return unit;
    }

    /// <summary>How many characters of <paramref name="text"/> come before the UTF-16 index <paramref name="units"/>.
    /// </summary>
    public static int Characters(string text, int units)
    {
        units = Math.Clamp(units, 0, text.Length);
        var characters = 0;
        for (var unit = 0; unit < units; characters++)
        {
            unit += char.IsSurrogatePair(text, unit) ? 2 : 1;
        }

        return characters;
    }
}
