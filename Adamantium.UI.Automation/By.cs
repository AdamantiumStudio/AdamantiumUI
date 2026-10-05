using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Automation;

/// <summary>What to look for: an element's id, name, control type or class, alone or together. It reads as text too -
/// <c>id=Cut</c>, <c>name="Cut out"</c>, <c>type=Button,name=OK</c> - and a path of them, <c>id=Shell/id=Cut</c>, finds
/// each one somewhere below the one before.</summary>
public sealed class By
{
    private static readonly string[] Keys = ["id", "name", "type", "class"];

    private readonly (string Key, string Value)[] _conditions;

    private By((string Key, string Value)[] conditions)
    {
        _conditions = conditions;
    }

    public static By Id(string id) => new([("id", id)]);

    public static By Name(string name) => new([("name", name)]);

    public static By Type(AutomationControlType type) => new([("type", type.ToString())]);

    public static By Class(string className) => new([("class", className)]);

    /// <summary>Both this and <paramref name="other"/>.</summary>
    public By And(By other) => new([.. _conditions, .. other._conditions]);

    public bool Matches(AutomationPeer peer) => _conditions.All(condition => condition.Key switch
    {
        "id" => peer.AutomationId == condition.Value,
        "name" => peer.Name == condition.Value,
        "type" => string.Equals(peer.ControlType.ToString(), condition.Value, StringComparison.OrdinalIgnoreCase),
        "class" => peer.ClassName == condition.Value,
        _ => false
    });

    public override string ToString() =>
        string.Join(",", _conditions.Select(condition => $"{condition.Key}={Quote(condition.Value)}"));

    internal IEnumerable<string> Values() => _conditions.Select(condition => condition.Value);

    public static string FormatPath(IEnumerable<By> path) => string.Join("/", path);

    /// <summary>Reads a selector path such as <c>id=Shell/type=Button,name="Cut out"</c>.</summary>
    /// <exception cref="FormatException">The text is not a selector path.</exception>
    public static IReadOnlyList<By> ParsePath(string text)
    {
        var path = new List<By>();
        var conditions = new List<(string Key, string Value)>();
        var at = 0;
        while (at < text.Length)
        {
            var equals = text.IndexOf('=', at);
            if (equals < 0)
            {
                throw new FormatException($"'{text}': expected key=value at {at}.");
            }

            var key = text[at..equals].Trim().ToLowerInvariant();
            if (!Keys.Contains(key))
            {
                throw new FormatException($"'{text}': '{key}' is not one of {string.Join(", ", Keys)}.");
            }

            at = equals + 1;
            conditions.Add((key, ReadValue(text, ref at)));

            if (at < text.Length && text[at] == '/')
            {
                path.Add(new By([.. conditions]));
                conditions.Clear();
            }

            at++;
        }

        if (conditions.Count == 0)
        {
            throw new FormatException($"'{text}' names nothing to look for.");
        }

        path.Add(new By([.. conditions]));
        return path;
    }

    private static string ReadValue(string text, ref int at)
    {
        if (at >= text.Length || text[at] != '"')
        {
            var end = text.IndexOfAny([',', '/'], at);
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
        value.Length > 0 && value.IndexOfAny([',', '/', '"', '\\']) < 0 && value.Trim() == value
            ? value
            : $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
}
