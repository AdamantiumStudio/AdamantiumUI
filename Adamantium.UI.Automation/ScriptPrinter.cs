using System;
using System.Text;

namespace Adamantium.UI.Automation;

internal static class ScriptPrinter
{
    public static string Line(ElementInfo element) => element.ToString();

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
}
