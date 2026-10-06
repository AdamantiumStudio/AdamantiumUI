using System;
using System.Text;

namespace Adamantium.UI.Automation;

/// <summary>What automation saw of one element at the moment it was asked.</summary>
public sealed class ElementInfo
{
    public int RuntimeId { get; set; }

    public string ControlType { get; set; }

    public string Name { get; set; }

    public string AutomationId { get; set; }

    public string ClassName { get; set; }

    /// <summary>The keys that reach it from the keyboard, such as a key tip; empty when none.</summary>
    public string AccessKey { get; set; }

    /// <summary>Where it stands, from its window down, for a person to read.</summary>
    public string Path { get; set; }

    public bool IsEnabled { get; set; }

    public bool IsOffscreen { get; set; }

    public bool HasKeyboardFocus { get; set; }

    /// <summary>Its rectangle on the screen in physical pixels: left, top, width, height.</summary>
    public double[] Bounds { get; set; }

    /// <summary>What it can do, by pattern name.</summary>
    public string[] Patterns { get; set; }

    /// <summary>Its value, when it holds one.</summary>
    public string Value { get; set; }

    /// <summary>Where its switch stands, when it has one.</summary>
    public string ToggleState { get; set; }

    /// <summary>Whether it is selected, when it can be.</summary>
    public bool? IsSelected { get; set; }

    /// <summary>Whether what it holds is open - Expanded, Collapsed or LeafNode - when it can open.</summary>
    public string ExpandCollapseState { get; set; }

    /// <summary>The lower limit of its number, when it holds one between limits.</summary>
    public double? Minimum { get; set; }

    /// <summary>The upper limit of its number, when it holds one between limits.</summary>
    public double? Maximum { get; set; }

    /// <summary>How far it is scrolled across, in percent; -1 when it cannot scroll that way.</summary>
    public double? HorizontalScroll { get; set; }

    /// <summary>How far it is scrolled down, in percent; -1 when it cannot scroll that way.</summary>
    public double? VerticalScroll { get; set; }

    /// <summary>Normal, Minimized or Maximized, when it is a window.</summary>
    public string WindowState { get; set; }

    /// <summary>How far it is zoomed, in percent, when it zooms.</summary>
    public double? Zoom { get; set; }

    /// <summary>Where it is docked - Top, Left, Bottom, Right, Fill or None - when it docks.</summary>
    public string DockPosition { get; set; }

    /// <summary>One line for a person: what it is, what it is called and found by, and the state it is in.</summary>
    public override string ToString()
    {
        var line = new StringBuilder(ControlType);
        if (!string.IsNullOrEmpty(Name))
        {
            line.Append(" \"").Append(Name).Append('"');
        }

        if (!string.IsNullOrEmpty(AutomationId))
        {
            line.Append(" #").Append(AutomationId);
        }

        line.Append(" [").Append(ClassName).Append(']');
        if (!string.IsNullOrEmpty(AccessKey))
        {
            line.Append(" key=").Append(AccessKey);
        }

        if (Value != null)
        {
            line.Append(" value=\"").Append(Value).Append('"');
        }

        if (ToggleState != null)
        {
            line.Append(' ').Append(ToggleState);
        }

        if (IsSelected == true)
        {
            line.Append(" selected");
        }

        if (ExpandCollapseState is { } state && state != "LeafNode")
        {
            line.Append(' ').Append(state);
        }

        if (Minimum is { } minimum && Maximum is { } maximum)
        {
            line.Append(FormattableString.Invariant($" in {minimum}..{maximum}"));
        }

        if (VerticalScroll is { } down && HorizontalScroll is { } across && (down >= 0 || across >= 0))
        {
            line.Append(FormattableString.Invariant($" scrolled {Percent(across)} across, {Percent(down)} down"));
        }

        if (WindowState != null)
        {
            line.Append(' ').Append(WindowState);
        }

        if (Zoom is { } zoom)
        {
            line.Append(" zoomed ").Append(Percent(zoom));
        }

        if (DockPosition != null)
        {
            line.Append(" docked ").Append(DockPosition);
        }

        if (IsOffscreen)
        {
            line.Append(" (offscreen)");
        }

        if (!IsEnabled)
        {
            line.Append(" (disabled)");
        }

        return line.ToString();
    }

    private static string Percent(double percent) => percent < 0 ? "-" : FormattableString.Invariant($"{percent}%");
}
