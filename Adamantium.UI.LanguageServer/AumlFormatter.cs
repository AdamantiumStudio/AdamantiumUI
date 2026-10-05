using System.Text;

namespace Adamantium.UI.LanguageServer;

/// <summary>
/// Lays out AUML and language files: an element per line, indented by level; a single attribute on the tag's line,
/// several one per line under the first; a <c>Setter</c> - and a language file's <c>Phrase</c> - on one line; an empty
/// element closed in place; at most one blank line between siblings. Attribute values, comments and text are kept as
/// written, and an element holding text keeps its content as it is.
/// </summary>
public static class AumlFormatter
{
    /// <summary>The whole text laid out, or null when it is not well-formed markup.</summary>
    public static string Format(string text, AumlFormatOptions options)
    {
        var (prefix, body) = SplitByteOrderMark(text);
        var nodes = Parse(body);
        if (nodes == null)
        {
            return null;
        }

        var newLine = NewLineOf(body);
        var output = new StringBuilder(prefix);
        for (var i = 0; i < nodes.Count; i++)
        {
            if (i > 0)
            {
                output.Append(newLine);
                if (nodes[i].BlankLineBefore)
                {
                    output.Append(newLine);
                }
            }

            Emit(nodes[i], 0, body, options, newLine, output);
        }

        if (body.EndsWith('\n'))
        {
            output.Append(newLine);
        }

        return output.ToString();
    }

    /// <summary>The edits that lay out the elements written within the given offsets - each whole, wherever it ends;
    /// empty when the text is not well-formed or already laid out.</summary>
    public static IReadOnlyList<AumlFormatEdit> FormatRange(string text, int start, int end, AumlFormatOptions options)
    {
        var (prefix, body) = SplitByteOrderMark(text);
        var nodes = Parse(body);
        if (nodes == null)
        {
            return [];
        }

        var spanStart = LineStart(body, Math.Clamp(start - prefix.Length, 0, body.Length));
        var spanEnd = LineEnd(body, Math.Clamp(end - prefix.Length, 0, body.Length));
        var edits = new List<AumlFormatEdit>();
        CollectRangeEdits(nodes, 0, spanStart, spanEnd, body, prefix.Length, options, NewLineOf(body), edits);
        return edits;
    }

    private static void CollectRangeEdits(List<FormatNode> nodes, int depth, int spanStart, int spanEnd, string text,
        int shift, AumlFormatOptions options, string newLine, List<AumlFormatEdit> edits)
    {
        foreach (var node in nodes)
        {
            if (node.Name == null || node.Start >= spanEnd || node.End <= spanStart)
            {
                continue;
            }

            if (node.Start < spanStart && node.TagEnd <= spanStart)
            {
                if (!HoldsText(node))
                {
                    CollectRangeEdits(node.Children, depth + 1, spanStart, spanEnd, text, shift, options, newLine, edits);
                }

                continue;
            }

            var output = new StringBuilder();
            Emit(node, depth, text, options, newLine, output);
            var lineStart = LineStart(text, node.Start);
            var startsLine = string.IsNullOrWhiteSpace(text[lineStart..node.Start]);
            var replaced = startsLine ? lineStart : node.Start;
            var formatted = startsLine ? output.ToString() : output.ToString().TrimStart();
            if (text[replaced..node.End] != formatted)
            {
                edits.Add(new AumlFormatEdit(replaced + shift, node.End + shift, formatted));
            }
        }
    }

    private static void Emit(FormatNode node, int depth, string text, AumlFormatOptions options, string newLine,
        StringBuilder output)
    {
        var indent = Indent(depth, options);
        output.Append(indent);
        if (node.Name == null)
        {
            output.Append(Shifted(node.Raw.Trim(), indent.Length - (node.Start - LineStart(text, node.Start)), newLine));
            return;
        }

        output.Append('<').Append(node.Name);
        var oneLine = node.Attributes.Count == 1 || IsSetter(node.Name) || (options.IsLanguageFile && node.Name == "Phrase");
        var aligned = newLine + indent + new string(' ', node.Name.Length + 2);
        for (var i = 0; i < node.Attributes.Count; i++)
        {
            output.Append(i == 0 || oneLine ? " " : aligned);
            output.Append(node.Attributes[i].Name).Append('=').Append(node.Attributes[i].Value);
        }

        var content = node.IsSelfClosing ? string.Empty : text[node.TagEnd..node.ContentEnd];
        if (node.IsSelfClosing || (node.Children.Count == 0 && (content.Length == 0 || IsLayoutWhitespace(content))))
        {
            output.Append("/>");
            return;
        }

        output.Append('>');
        if (HoldsText(node) || node.Children.Count == 0)
        {
            output.Append(content);
        }
        else
        {
            foreach (var child in node.Children)
            {
                output.Append(newLine);
                if (child.BlankLineBefore)
                {
                    output.Append(newLine);
                }

                Emit(child, depth + 1, text, options, newLine, output);
            }

            output.Append(newLine);
            if (node.BlankLineBeforeEnd)
            {
                output.Append(newLine);
            }

            output.Append(indent);
        }

        output.Append("</").Append(node.Name).Append('>');
    }

    private static List<FormatNode> Parse(string text)
    {
        var top = new List<FormatNode>();
        var open = new Stack<FormatNode>();
        var newlines = 0;
        var i = 0;
        while (i < text.Length)
        {
            var siblings = open.Count > 0 ? open.Peek().Children : top;
            if (text[i] != '<')
            {
                var next = text.IndexOf('<', i);
                next = next < 0 ? text.Length : next;
                var chunk = text[i..next];
                if (string.IsNullOrWhiteSpace(chunk))
                {
                    newlines += chunk.Count(c => c == '\n');
                }
                else
                {
                    siblings.Add(new FormatNode { Raw = chunk, IsText = true, Start = i, End = next });
                    newlines = 0;
                }

                i = next;
                continue;
            }

            if (Follows(text, i, "</"))
            {
                var close = text.IndexOf('>', i);
                if (close < 0 || open.Count == 0 || open.Peek().Name != text[(i + 2)..close].Trim())
                {
                    return null;
                }

                var element = open.Pop();
                element.ContentEnd = i;
                element.End = close + 1;
                element.BlankLineBeforeEnd = newlines >= 2 && element.Children.Count > 0;
                newlines = 0;
                i = close + 1;
                continue;
            }

            var node = Follows(text, i, "<!--") ? Verbatim(text, i, "-->", false)
                : Follows(text, i, "<![CDATA[") ? Verbatim(text, i, "]]>", true)
                : Follows(text, i, "<?") ? Verbatim(text, i, "?>", false)
                : Follows(text, i, "<!") ? Verbatim(text, i, ">", false)
                : StartTag(text, i);
            if (node == null)
            {
                return null;
            }

            node.BlankLineBefore = newlines >= 2;
            newlines = 0;
            siblings.Add(node);
            if (node.Name != null && !node.IsSelfClosing)
            {
                open.Push(node);
                i = node.TagEnd;
            }
            else
            {
                i = node.End;
            }
        }

        return open.Count == 0 ? top : null;
    }

    private static FormatNode Verbatim(string text, int start, string terminator, bool isText)
    {
        var close = text.IndexOf(terminator, start + 2, StringComparison.Ordinal);
        if (close < 0)
        {
            return null;
        }

        var end = close + terminator.Length;
        return new FormatNode { Raw = text[start..end], IsText = isText, Start = start, End = end };
    }

    private static FormatNode StartTag(string text, int start)
    {
        var i = start + 1;
        while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('>' or '/'))
        {
            i++;
        }

        if (i == start + 1)
        {
            return null;
        }

        var node = new FormatNode { Name = text[(start + 1)..i], Start = start };
        while (true)
        {
            i = SkipWhitespace(text, i);
            if (i >= text.Length)
            {
                return null;
            }

            if (text[i] == '>')
            {
                node.TagEnd = i + 1;
                return node;
            }

            if (text[i] == '/')
            {
                if (!Follows(text, i, "/>"))
                {
                    return null;
                }

                node.IsSelfClosing = true;
                node.TagEnd = node.End = i + 2;
                return node;
            }

            var nameStart = i;
            while (i < text.Length && text[i] != '=' && !char.IsWhiteSpace(text[i]) && text[i] is not ('>' or '/'))
            {
                i++;
            }

            var name = text[nameStart..i];
            i = SkipWhitespace(text, i);
            if (name.Length == 0 || i >= text.Length || text[i] != '=')
            {
                return null;
            }

            i = SkipWhitespace(text, i + 1);
            if (i >= text.Length || text[i] is not ('"' or '\''))
            {
                return null;
            }

            var close = text.IndexOf(text[i], i + 1);
            if (close < 0)
            {
                return null;
            }

            node.Attributes.Add((name, text[i..(close + 1)]));
            i = close + 1;
        }
    }

    private static string Shifted(string raw, int shift, string newLine)
    {
        var lines = raw.Replace("\r\n", "\n").Split('\n');
        for (var i = 1; i < lines.Length && shift != 0; i++)
        {
            if (shift > 0)
            {
                lines[i] = new string(' ', shift) + lines[i];
            }
            else
            {
                var leading = lines[i].Length - lines[i].TrimStart(' ').Length;
                lines[i] = lines[i][Math.Min(leading, -shift)..];
            }
        }

        return string.Join(newLine, lines);
    }

    private static bool HoldsText(FormatNode node) => node.Children.Any(c => c.IsText);

    private static bool IsSetter(string name) => name == "Setter" || name.EndsWith(":Setter", StringComparison.Ordinal);

    private static bool IsLayoutWhitespace(string content) => string.IsNullOrWhiteSpace(content) && content.Contains('\n');

    private static string Indent(int depth, AumlFormatOptions options) =>
        options.InsertSpaces ? new string(' ', depth * Math.Max(1, options.TabSize)) : new string('\t', depth);

    private static string NewLineOf(string text) => text.Contains("\r\n") ? "\r\n" : "\n";

    private static (string Prefix, string Body) SplitByteOrderMark(string text) =>
        text.StartsWith('﻿') ? ("﻿", text[1..]) : (string.Empty, text);

    private static bool Follows(string text, int index, string value) =>
        string.CompareOrdinal(text, index, value, 0, value.Length) == 0;

    private static int SkipWhitespace(string text, int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        return index;
    }

    private static int LineStart(string text, int offset) => offset <= 0 ? 0 : text.LastIndexOf('\n', offset - 1) + 1;

    private static int LineEnd(string text, int offset)
    {
        var newline = text.IndexOf('\n', offset);
        return newline < 0 ? text.Length : newline;
    }
}
