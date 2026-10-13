using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Adamantium.UI.Automation;

internal static class SelectorSyntax
{
    public static readonly string[] Keys = ["id", "name", "type", "class"];
    public static readonly string[] Relations = ["parent", "next", "previous"];

    public static List<Step> Parse(string text)
    {
        var path = new List<Step>();
        var at = 0;
        var child = false;
        while (true)
        {
            var step = ReadStep(text, ref at, child);
            if (path.Count == 0 && step.Relation != null)
            {
                throw new FormatException($"'{text}': a path starts with something to look for, not '{step.Relation}'.");
            }

            path.Add(step);
            if (at >= text.Length)
            {
                return path;
            }

            if (text[at] is not ('/' or '>'))
            {
                throw new FormatException($"'{text}': expected / or > at {at}.");
            }

            child = text[at] == '>';
            at++;
        }
    }

    private static Step ReadStep(string text, ref int at, bool child)
    {
        var start = at;
        var relation = Relations.FirstOrDefault(word => string.Compare(text, start, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) == 0
                                                        && (start + word.Length == text.Length || text[start + word.Length] is '/' or '>' or '['));
        if (relation != null)
        {
            at += relation.Length;
            return new Step([], ReadIndex(text, ref at), child, relation);
        }

        var conditions = new List<(string Key, string Value)>();
        while (true)
        {
            var equals = text.IndexOf('=', at);
            if (equals < 0)
            {
                throw new FormatException($"'{text}': expected key=value at {at}.");
            }

            var key = text.Substring(at, equals - at).Trim().ToLowerInvariant();
            if (!Keys.Contains(key))
            {
                throw new FormatException($"'{text}': '{key}' is not one of {string.Join(", ", Keys)}, nor " +
                                          $"{string.Join(", ", Relations)}.");
            }

            at = equals + 1;
            conditions.Add((key, ReadValue(text, ref at)));
            if (at < text.Length && text[at] == ',')
            {
                at++;
                continue;
            }

            break;
        }

        if (conditions.Count == 0 || at == start)
        {
            throw new FormatException($"'{text}' names nothing to look for.");
        }

        return new Step([.. conditions], ReadIndex(text, ref at), child, null);
    }

    private static int? ReadIndex(string text, ref int at)
    {
        if (at >= text.Length || text[at] != '[')
        {
            return null;
        }

        var close = text.IndexOf(']', at);
        if (close < 0 || !int.TryParse(text.Substring(at + 1, close - at - 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var index))
        {
            throw new FormatException($"'{text}': expected [number] at {at}.");
        }

        at = close + 1;
        return index;
    }

    private static string ReadValue(string text, ref int at)
    {
        if (at >= text.Length || text[at] != '"')
        {
            var end = text.IndexOfAny([',', '/', '>', '['], at);
            if (end < 0)
            {
                end = text.Length;
            }

            var bare = text.Substring(at, end - at).Trim();
            at = end;
            return bare;
        }

        var value = new StringBuilder();
        for (at++; at < text.Length; at++)
        {
            if (text[at] == '\\' && at + 1 < text.Length)
            {
                value.Append(text[++at]);
            }
            else if (text[at] == '"')
            {
                at++;
                return value.ToString();
            }
            else
            {
                value.Append(text[at]);
            }
        }

        throw new FormatException($"'{text}': a quote is not closed.");
    }

    internal readonly struct Step((string Key, string Value)[] conditions, int? index, bool isChild, string relation)
    {
        public (string Key, string Value)[] Conditions { get; } = conditions;
        public int? Index { get; } = index;
        public bool IsChild { get; } = isChild;
        public string Relation { get; } = relation;
    }
}
