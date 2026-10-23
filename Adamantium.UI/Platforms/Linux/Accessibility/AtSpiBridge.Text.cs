using System;
using System.Collections.Generic;
using System.Text;
using Adamantium.DBus;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Platforms.Linux.Accessibility;

internal sealed partial class AtSpiBridge
{
    private enum TextUnit : uint
    {
        Character = 0,
        Word = 1,
        Sentence = 2,
        Line = 3,
        Paragraph = 4,
    }

    private enum TextBoundary : uint
    {
        Character = 0,
        WordStart = 1,
        WordEnd = 2,
        SentenceStart = 3,
        SentenceEnd = 4,
        LineStart = 5,
        LineEnd = 6,
    }

    private static AtSpiText TextOf(AutomationPeer peer) => AtSpiText.Of(peer);

    private static DBusMessage Text(DBusMessage call, AutomationPeer peer)
    {
        if (TextOf(peer) is not { } text)
        {
            return null;
        }

        var reader = call.Reader();
        switch (call.Member)
        {
            case Methods.GetText:
            {
                var (start, end) = Clamp(text, reader.ReadInt32(), reader.ReadInt32());
                return DBusMessage.MethodReturn(call, Signatures.String, writer => writer.WriteString(text.Substring(start, end)));
            }
            case Methods.GetCharacterAtOffset:
            {
                var character = text.CharacterAt(reader.ReadInt32());
                return DBusMessage.MethodReturn(call, Signatures.Int32, writer => writer.WriteInt32(character));
            }
            case Methods.SetCaretOffset:
            {
                var offset = Math.Clamp(reader.ReadInt32(), 0, text.Length);
                return Answered(call, text.CanSelect && Done(() => text.Select(offset, offset)));
            }
            case Methods.GetTextAtOffset or Methods.GetTextBeforeOffset or Methods.GetTextAfterOffset:
            {
                var offset = reader.ReadInt32();
                var unit = UnitOf((TextBoundary)reader.ReadUInt32());
                var range = RangeAt(text, offset, unit);
                if (call.Member == Methods.GetTextBeforeOffset)
                {
                    range = range.Start > 0 ? RangeAt(text, range.Start - 1, unit) : (0, 0);
                }
                else if (call.Member == Methods.GetTextAfterOffset)
                {
                    range = range.End < text.Length ? RangeAt(text, range.End, unit) : (text.Length, text.Length);
                }

                return Segment(call, text, range);
            }
            case Methods.GetStringAtOffset:
            {
                var offset = reader.ReadInt32();
                var unit = (TextUnit)Math.Min(reader.ReadUInt32(), (uint)TextUnit.Paragraph);
                return Segment(call, text, RangeAt(text, offset, unit));
            }
            case Methods.GetNSelections:
                return DBusMessage.MethodReturn(call, Signatures.Int32,
                    writer => writer.WriteInt32(text.SelectionEnd > text.SelectionStart ? 1 : 0));
            case Methods.GetSelection:
                return DBusMessage.MethodReturn(call, Signatures.Point, writer =>
                {
                    writer.WriteInt32(text.SelectionStart);
                    writer.WriteInt32(text.SelectionEnd);
                });
            case Methods.AddSelection or Methods.SetSelection:
            {
                if (call.Member == Methods.SetSelection)
                {
                    reader.ReadInt32();
                }

                var (start, end) = Clamp(text, reader.ReadInt32(), reader.ReadInt32());
                return Answered(call, text.CanSelect && Done(() => text.Select(start, end)));
            }
            case Methods.RemoveSelection:
            {
                var caret = text.Caret;
                return Answered(call, text.CanSelect && Done(() => text.Select(caret, caret)));
            }
            case Methods.GetAttributes or Methods.GetAttributeRun:
                return DBusMessage.MethodReturn(call, Signatures.AttributeRun, writer =>
                {
                    writer.WriteArray(8, _ => { });
                    writer.WriteInt32(0);
                    writer.WriteInt32(text.Length);
                });
            case Methods.GetDefaultAttributes or Methods.GetDefaultAttributeSet:
                return DBusMessage.MethodReturn(call, Signatures.Attributes, writer => writer.WriteArray(8, _ => { }));
            case Methods.GetAttributeValue:
                return DBusMessage.MethodReturn(call, Signatures.String, writer => writer.WriteString(string.Empty));
            case Methods.GetCharacterExtents:
            {
                var offset = Math.Clamp(reader.ReadInt32(), 0, text.Length);
                var bounds = Union(text.Bounds(offset, Math.Min(offset + 1, text.Length)));
                return Extents(call, Logical(peer, bounds, reader.ReadUInt32()));
            }
            case Methods.GetRangeExtents:
            {
                var (start, end) = Clamp(text, reader.ReadInt32(), reader.ReadInt32());
                return Extents(call, Logical(peer, Union(text.Bounds(start, end)), reader.ReadUInt32()));
            }
            case Methods.GetOffsetAtPoint:
            {
                var (x, y) = (reader.ReadInt32(), reader.ReadInt32());
                var index = text.IndexAt(Physical(peer, x, y, reader.ReadUInt32()));
                return DBusMessage.MethodReturn(call, Signatures.Int32, writer => writer.WriteInt32(index));
            }
            case Methods.GetBoundedRanges:
                return DBusMessage.MethodReturn(call, Signatures.BoundedRanges, writer => writer.WriteArray(8, _ => { }));
            case Methods.ScrollSubstringTo:
            {
                var offset = Math.Clamp(reader.ReadInt32(), 0, text.Length);
                return Answered(call, text.CanSelect && Done(() => text.ScrollTo(offset)));
            }
            case Methods.ScrollSubstringToPoint:
                return Answered(call, false);
            default:
                return null;
        }
    }

    private static DBusMessage EditableText(DBusMessage call, AutomationPeer peer)
    {
        if (peer.IsPassword || peer.GetPattern(PatternId.Value) is not IValueProvider { IsReadOnly: false } value ||
            TextOf(peer) is not { } text)
        {
            return null;
        }

        var reader = call.Reader();
        switch (call.Member)
        {
            case Methods.SetTextContents:
            {
                var contents = reader.ReadString();
                return Answered(call, Done(() => value.SetValue(contents)));
            }
            case Methods.InsertText:
            {
                var position = AtSpiText.Units(text.Content, Math.Clamp(reader.ReadInt32(), 0, text.Length));
                var inserted = Utf8Prefix(reader.ReadString(), reader.ReadInt32());
                return Answered(call, inserted.Length > 0 && Done(() => value.SetValue(text.Content.Insert(position, inserted))));
            }
            case Methods.DeleteText or Methods.CutText or Methods.CopyText:
            {
                var (start, end) = Clamp(text, reader.ReadInt32(), reader.ReadInt32());
                var piece = text.Substring(start, end);
                if (call.Member != Methods.DeleteText && piece.Length > 0)
                {
                    Clipboard.SetText(piece);
                }

                if (call.Member == Methods.CopyText)
                {
                    return DBusMessage.MethodReturn(call);
                }

                var from = AtSpiText.Units(text.Content, start);
                return Answered(call, piece.Length > 0 && Done(() => value.SetValue(text.Content.Remove(from, piece.Length))));
            }
            case Methods.PasteText:
            {
                var position = AtSpiText.Units(text.Content, Math.Clamp(reader.ReadInt32(), 0, text.Length));
                var pasted = Clipboard.GetText() ?? string.Empty;
                return Answered(call, pasted.Length > 0 && Done(() => value.SetValue(text.Content.Insert(position, pasted))));
            }
            default:
                return null;
        }
    }

    private static string Utf8Prefix(string text, int bytes)
    {
        if (bytes < 0 || bytes >= Encoding.UTF8.GetByteCount(text))
        {
            return text;
        }

        var units = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            bytes -= rune.Utf8SequenceLength;
            if (bytes < 0)
            {
                break;
            }

            units += rune.Utf16SequenceLength;
        }

        return text[..units];
    }

    private static TextUnit UnitOf(TextBoundary boundary) => boundary switch
    {
        TextBoundary.Character => TextUnit.Character,
        TextBoundary.WordStart or TextBoundary.WordEnd => TextUnit.Word,
        TextBoundary.SentenceStart or TextBoundary.SentenceEnd => TextUnit.Sentence,
        _ => TextUnit.Line,
    };

    private static (int Start, int End) RangeAt(AtSpiText text, int offset, TextUnit unit)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        if (text.Length == 0)
        {
            return (0, 0);
        }

        if (unit == TextUnit.Character)
        {
            return offset < text.Length ? (offset, offset + 1) : (offset, offset);
        }

        if (unit == TextUnit.Line)
        {
            return text.LineAt(Math.Min(offset, text.Length - 1));
        }

        var content = text.Content;
        var at = AtSpiText.Units(content, Math.Min(offset, text.Length - 1));
        var start = at;
        while (start > 0 && !StartsUnit(content, start, unit))
        {
            start--;
        }

        var end = at + 1;
        while (end < content.Length && !StartsUnit(content, end, unit))
        {
            end++;
        }

        return (AtSpiText.Characters(content, start), AtSpiText.Characters(content, end));
    }

    private static bool StartsUnit(string content, int index, TextUnit unit) => unit switch
    {
        TextUnit.Word => char.IsLetterOrDigit(content, index) && !char.IsLowSurrogate(content[index]) &&
                         !char.IsLetterOrDigit(content[index - 1]) && !char.IsHighSurrogate(content[index - 1]),
        TextUnit.Paragraph => content[index - 1] == '\n',
        _ => StartsSentence(content, index),
    };

    private static bool StartsSentence(string content, int index)
    {
        if (content[index - 1] == '\n')
        {
            return true;
        }

        if (char.IsWhiteSpace(content[index]) || !char.IsWhiteSpace(content[index - 1]))
        {
            return false;
        }

        var before = index - 1;
        while (before >= 0 && char.IsWhiteSpace(content[before]) && content[before] != '\n')
        {
            before--;
        }

        return before < 0 || content[before] is '.' or '!' or '?' or '\n';
    }

    private static (int Start, int End) Clamp(AtSpiText text, int start, int end)
    {
        if (end < 0 || end > text.Length)
        {
            end = text.Length;
        }

        start = Math.Clamp(start, 0, end);
        return (start, end);
    }

    private static DBusMessage Segment(DBusMessage call, AtSpiText text, (int Start, int End) range) =>
        DBusMessage.MethodReturn(call, Signatures.TextSegment, writer =>
        {
            writer.WriteString(text.Substring(range.Start, range.End));
            writer.WriteInt32(range.Start);
            writer.WriteInt32(range.End);
        });

    private static Rect Union(IReadOnlyList<Rect> rects)
    {
        if (rects.Count == 0)
        {
            return Rect.Empty;
        }

        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
        foreach (var rect in rects)
        {
            left = Math.Min(left, rect.X);
            top = Math.Min(top, rect.Y);
            right = Math.Max(right, rect.X + rect.Width);
            bottom = Math.Max(bottom, rect.Y + rect.Height);
        }

        return new Rect(left, top, right - left, bottom - top);
    }

    private static DBusMessage Extents(DBusMessage call, Rect bounds) =>
        DBusMessage.MethodReturn(call, Signatures.Extents, writer => WriteExtents(writer, bounds));

    private static void WriteExtents(DBusWriter writer, Rect bounds)
    {
        writer.WriteInt32((int)Math.Round(bounds.X));
        writer.WriteInt32((int)Math.Round(bounds.Y));
        writer.WriteInt32((int)Math.Round(bounds.Width));
        writer.WriteInt32((int)Math.Round(bounds.Height));
    }

    private static DBusMessage Answered(DBusMessage call, bool done) =>
        DBusMessage.MethodReturn(call, Signatures.Boolean, writer => writer.WriteBoolean(done));

    private static bool Done(System.Action change)
    {
        try
        {
            change();
            return true;
        }
        catch (Exception e)
        {
            Serilog.Log.Logger.Debug(e, "An assistive technology's change was refused");
            return false;
        }
    }
}
