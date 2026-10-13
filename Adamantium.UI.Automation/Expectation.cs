using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Adamantium.UI.Automation;

/// <summary>What an element is expected to show: a key - <c>name</c>, <c>value</c>, <c>toggle</c>, <c>zoom</c>... or the
/// name of any of its control's properties - and the text it should read as.</summary>
public readonly record struct Expectation(string Key, string Value)
{
    /// <summary>The keys read off the element itself; any other key names a property of its control.</summary>
    public static IReadOnlyList<string> ElementKeys { get; } =
    [
        "name", "id", "type", "class", "value", "toggle", "selected", "expanded", "min", "max", "hscroll", "vscroll",
        "window", "zoom", "dock", "key", "left", "top", "width", "height", "enabled", "offscreen", "focus"
    ];

    internal static bool IsElementKey(string key) => ElementKeys.Contains(key);

    internal static string Actual(ElementDetails details, string key)
    {
        var element = details.Element;
        return key switch
        {
            "name" => element.Name,
            "id" => element.AutomationId,
            "type" => element.ControlType,
            "class" => element.ClassName,
            "value" => element.Value,
            "toggle" => element.ToggleState,
            "selected" => element.IsSelected?.ToString(),
            "expanded" => element.ExpandCollapseState,
            "min" => Invariant(element.Minimum),
            "max" => Invariant(element.Maximum),
            "hscroll" => Invariant(element.HorizontalScroll),
            "vscroll" => Invariant(element.VerticalScroll),
            "window" => element.WindowState,
            "dock" => element.DockPosition,
            "zoom" => Invariant(element.Zoom),
            "key" => element.AccessKey,
            "left" => Invariant(Math.Round(element.Bounds[0])),
            "top" => Invariant(Math.Round(element.Bounds[1])),
            "width" => Invariant(Math.Round(element.Bounds[2])),
            "height" => Invariant(Math.Round(element.Bounds[3])),
            "enabled" => element.IsEnabled.ToString(),
            "offscreen" => element.IsOffscreen.ToString(),
            "focus" => element.HasKeyboardFocus.ToString(),
            _ => Unquote(details.Properties.FirstOrDefault(property => property.Name == key)?.Value)
        };
    }

    internal static List<string> Misses(ElementDetails details, IEnumerable<Expectation> expected) =>
        expected
            .Select(expectation => (expectation.Key, expectation.Value, Actual: Actual(details, expectation.Key)))
            .Where(check => check.Actual != check.Value)
            .Select(check => $"  {check.Key}: expected \"{check.Value}\", was \"{check.Actual}\"")
            .ToList();

    private static string Invariant(double? number) => number?.ToString(CultureInfo.InvariantCulture);

    private static string Unquote(string text) =>
        text is { Length: >= 2 } && text[0] == '"' && text[^1] == '"' ? text[1..^1] : text;
}
