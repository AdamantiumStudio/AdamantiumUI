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
    public static IReadOnlyList<By> ParsePath(string text) =>
        SelectorSyntax.Parse(text).Select(step => new By(step.Conditions, step.Index, step.IsChild, step.Relation)).ToList();

    private static string Quote(string value) =>
        value.Length > 0 && value.IndexOfAny([',', '/', '>', '[', '"', '\\']) < 0 && value.Trim() == value
            ? value
            : $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
}
