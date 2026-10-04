using System;
using System.Text;

namespace Adamantium.UI.Automation.Cli;

internal static class Printer
{
    public static string Line(ElementInfo element)
    {
        var line = new StringBuilder(element.ControlType);
        if (!string.IsNullOrEmpty(element.Name))
        {
            line.Append(" \"").Append(element.Name).Append('"');
        }

        if (!string.IsNullOrEmpty(element.AutomationId))
        {
            line.Append(" #").Append(element.AutomationId);
        }

        line.Append(" [").Append(element.ClassName).Append(']');
        if (element.Value != null)
        {
            line.Append(" value=\"").Append(element.Value).Append('"');
        }

        if (element.ToggleState != null)
        {
            line.Append(' ').Append(element.ToggleState);
        }

        if (element.IsSelected == true)
        {
            line.Append(" selected");
        }

        if (element.IsOffscreen)
        {
            line.Append(" (offscreen)");
        }

        if (!element.IsEnabled)
        {
            line.Append(" (disabled)");
        }

        return line.ToString();
    }

    public static string Details(ElementInfo element)
    {
        var bounds = element.Bounds;
        return string.Join(Environment.NewLine,
            Line(element),
            $"  path:     {element.Path}",
            $"  patterns: {string.Join(", ", element.Patterns ?? [])}",
            $"  bounds:   {bounds[0]:0},{bounds[1]:0} {bounds[2]:0}x{bounds[3]:0} px",
            $"  focus:    {(element.HasKeyboardFocus ? "has keyboard focus" : "no")}");
    }
}
