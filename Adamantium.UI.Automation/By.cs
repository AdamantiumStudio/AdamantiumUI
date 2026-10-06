using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Automation;

/// <summary>One step of a path to an element: what to look for - an element's id, name, control type or class, alone or
/// together - or where to go from the element before: its parent, the sibling after it or before it. It reads as text
/// too: <c>id=Cut</c>, <c>name="Cut out"</c>, <c>type=Button,name=OK</c>. In a path, <c>/</c> looks anywhere below the
/// element before and <c>&gt;</c> only among its children; <c>[n]</c> keeps the n-th match, from 0, <c>[-1]</c> the last:
/// <c>id=Orders&gt;type=DataItem[2]/name=Delete</c>, <c>id=Cut/next</c>, <c>id=Cut/parent</c>.</summary>
public sealed class By
{
    private static readonly string[] Keys = ["id", "name", "type", "class"];
    private static readonly string[] Relations = ["parent", "next", "previous"];

    private readonly (string Key, string Value)[] _conditions;

    private By((string Key, string Value)[] conditions, int? index = null, bool child = false, string relation = null)
    {
        _conditions = conditions;
        Index = index;
        IsChild = child;
        Relation = relation;
    }

    public static By Id(string id) => new([("id", id)]);

    public static By Name(string name) => new([("name", name)]);

    public static By Type(AutomationControlType type) => new([("type", type.ToString())]);

    public static By Class(string className) => new([("class", className)]);

    /// <summary>The element that holds the one before.</summary>
    public static By Parent { get; } = new([], relation: "parent");

    /// <summary>The sibling after the element before.</summary>
    public static By Next { get; } = new([], relation: "next");

    /// <summary>The sibling before the element before.</summary>
    public static By Previous { get; } = new([], relation: "previous");

    /// <summary>Which match the step keeps, from 0; negative counts from the last. Null keeps them all.</summary>
    public int? Index { get; }

    /// <summary>Whether the step looks only among the children of the element before, not anywhere below it.</summary>
    public bool IsChild { get; }

    /// <summary><c>parent</c>, <c>next</c> or <c>previous</c> for a step that goes from the element before; null for one
    /// that looks for something.</summary>
    public string Relation { get; }

    /// <summary>Both this and <paramref name="other"/>.</summary>
    public By And(By other) => new([.. _conditions, .. other._conditions], Index ?? other.Index, IsChild);

    /// <summary>Only the <paramref name="index"/>-th match, from 0; -1 for the last.</summary>
    public By At(int index) => new(_conditions, index, IsChild, Relation);

    internal By AsChild() => new(_conditions, Index, true, Relation);

    public bool Matches(AutomationPeer peer) => _conditions.All(condition => condition.Key switch
    {
        "id" => peer.AutomationId == condition.Value,
        "name" => peer.Name == condition.Value,
        "type" => string.Equals(peer.ControlType.ToString(), condition.Value, StringComparison.OrdinalIgnoreCase),
        "class" => peer.ClassName == condition.Value,
        _ => false
    });

    public override string ToString() =>
        (Relation ?? string.Join(",", _conditions.Select(condition => $"{condition.Key}={Quote(condition.Value)}"))) +
        (Index is { } index ? $"[{index.ToString(CultureInfo.InvariantCulture)}]" : string.Empty);

    internal IEnumerable<string> Values() => _conditions.Select(condition => condition.Value);

    public static string FormatPath(IEnumerable<By> path)
    {
        var text = new StringBuilder();
        foreach (var step in path)
        {
            if (text.Length > 0)
            {
                text.Append(step.IsChild ? '>' : '/');
            }

            text.Append(step);
        }

        return text.ToString();
    }

    /// <summary>Reads a selector path such as <c>id=Shell/type=Button,name="Cut out"</c> or
    /// <c>id=Orders&gt;type=DataItem[-1]/next</c>.</summary>
    /// <exception cref="FormatException">The text is not a selector path.</exception>
    public static IReadOnlyList<By> ParsePath(string text)
    {
        var path = new List<By>();
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

    private static By ReadStep(string text, ref int at, bool child)
    {
        var start = at;
        var relation = Relations.FirstOrDefault(word => string.Compare(text, start, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) == 0
                                                        && (start + word.Length == text.Length || text[start + word.Length] is '/' or '>' or '['));
        if (relation != null)
        {
            at += relation.Length;
            return new By([], ReadIndex(text, ref at), child, relation);
        }

        var conditions = new List<(string Key, string Value)>();
        while (true)
        {
            var equals = text.IndexOf('=', at);
            if (equals < 0)
            {
                throw new FormatException($"'{text}': expected key=value at {at}.");
            }

            var key = text[at..equals].Trim().ToLowerInvariant();
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

        return new By([.. conditions], ReadIndex(text, ref at), child);
    }

    private static int? ReadIndex(string text, ref int at)
    {
        if (at >= text.Length || text[at] != '[')
        {
            return null;
        }

        var close = text.IndexOf(']', at);
        if (close < 0 || !int.TryParse(text[(at + 1)..close], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var index))
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

            var bare = text[at..end].Trim();
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

    private static string Quote(string value) =>
        value.Length > 0 && value.IndexOfAny([',', '/', '>', '[', '"', '\\']) < 0 && value.Trim() == value
            ? value
            : $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
}
