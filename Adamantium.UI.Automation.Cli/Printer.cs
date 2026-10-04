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

        if (element.ExpandCollapseState is { } state && state != "LeafNode")
        {
            line.Append(' ').Append(state);
        }

        if (element.Minimum is { } minimum && element.Maximum is { } maximum)
        {
            line.Append(FormattableString.Invariant($" in {minimum}..{maximum}"));
        }

        if (element.VerticalScroll is { } down && element.HorizontalScroll is { } across)
        {
            line.Append(FormattableString.Invariant($" scrolled {Percent(across)} across, {Percent(down)} down"));
        }

        if (element.WindowState != null)
        {
            line.Append(' ').Append(element.WindowState);
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

    public static string Error(ErrorEntry error) => $"#{error.Sequence} [{error.Kind}] {error.Message}";

    public static string Inspection(ElementDetails details)
    {
        var text = new StringBuilder(Details(details.Element)).AppendLine();
        text.AppendLine($"  layout:   {details.Layout}");
        text.AppendLine($"  parents:  visual {details.VisualParent ?? "-"}, logical {details.LogicalParent ?? "-"}, templated {details.TemplatedParent ?? "-"}");
        text.AppendLine($"  data:     {details.DataContext ?? "-"}");
        text.AppendLine("  properties:");
        foreach (var property in details.Properties)
        {
            text.AppendLine($"    {property.Name} = {property.Value}  ({property.Source})");
        }

        if (details.Bindings.Count > 0)
        {
            text.AppendLine("  bindings:");
            foreach (var binding in details.Bindings)
            {
                text.Append($"    {binding.Property} <- {binding.Binding}: {binding.Status}");
                text.AppendLine(binding.Failure == null ? string.Empty : $" - {binding.Failure}");
            }
        }

        return text.ToString().TrimEnd();
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

    private static string Percent(double percent) => percent < 0 ? "-" : FormattableString.Invariant($"{percent}%");
}
