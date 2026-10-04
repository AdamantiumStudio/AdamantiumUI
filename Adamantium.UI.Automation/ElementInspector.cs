using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Adorners;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Automation;

internal static class ElementInspector
{
    public static ElementDetails Inspect(UIComponent element, ElementInfo info, string[] names)
    {
        var properties = names is { Length: > 0 }
            ? names.Select(name => AdamantiumPropertyMap.ResolveProperty(element.GetType(), name)
                ?? throw new AutomationException($"{element.GetType().Name} has no property '{name}'."))
            : AdamantiumPropertyMap.GetRegistered(element).Where(property => element.GetValueSource(property) != ValuePriority.Default);

        return new ElementDetails
        {
            Element = info,
            Properties = [.. properties.OrderBy(property => property.Name).Select(property => new PropertyValueInfo
            {
                Name = property.Name,
                Value = ValueOf(element, property),
                Source = element.GetValueSource(property).ToString()
            })],
            Bindings = [.. BindingEngine.GetBindings(element).Select(binding => new BindingInfo
            {
                Property = binding.TargetProperty?.Name,
                Binding = Describe(binding.BindingBase),
                Status = binding.Status.ToString(),
                Failure = binding.Status is BindingStatus.PathError or BindingStatus.UpdateTargetError or BindingStatus.UpdateSourceError
                    ? binding.Failure
                    : null
            })],
            Layout = Layout(element),
            VisualParent = Describe(element.VisualParent),
            LogicalParent = Describe(element.LogicalParent),
            TemplatedParent = Describe(element.TemplatedParent),
            DataContext = element.DataContext?.GetType().FullName
        };
    }

    public static string Visual(UIComponent root, int depth)
    {
        var text = new StringBuilder();
        Write(root, 0);
        return text.ToString();

        void Write(IUIComponent element, int level)
        {
            text.Append(' ', level * 2).Append(Describe(element)).Append("  ").Append(Layout(element));
            if (element is TextBlock { Text: { Length: > 0 } words })
            {
                text.Append("  \"").Append(words).Append('"');
            }

            text.AppendLine();
            if (depth > 0 && level + 1 >= depth)
            {
                return;
            }

            foreach (var child in element.VisualChildren)
            {
                Write(child, level + 1);
            }
        }
    }

    public static string State(IReadOnlyList<IWindow> windows)
    {
        var text = new StringBuilder();
        text.Append("focus: ").AppendLine(Describe(KeyboardDevice.CurrentDevice.FocusedComponent) ?? "nothing");
        text.Append("mouse over: ").AppendLine(Describe(MouseDevice.CurrentDevice.DirectlyOver) ?? "nothing");
        foreach (var window in windows)
        {
            text.Append("window: ").Append(Describe(window)).Append(" \"").Append(window.Title).Append('"');
            text.AppendLine(window.IsActive ? " active" : string.Empty);
            foreach (var popup in window.PopupRoots)
            {
                text.Append("  popup: ").AppendLine(Describe(popup));
            }

            foreach (var adorner in (window as WindowBase)?.Adorners.OfType<Adorner>() ?? [])
            {
                text.Append("  adorner: ").Append(adorner.GetType().Name);
                if (adorner is KeyTipAdorner keyTip)
                {
                    text.Append(" \"").Append(keyTip.Keys).Append('"');
                }

                text.Append(" on ").Append(Describe(adorner.AdornedElement) ?? "nothing");
                if ((adorner.AdornedElement as UIComponent)?.GetAutomationPeer()?.Name is { Length: > 0 } name)
                {
                    text.Append(" \"").Append(name).Append('"');
                }

                text.AppendLine();
            }
        }

        return text.ToString();
    }

    private static string Layout(IUIComponent element)
    {
        var size = element.RenderSize;
        var bounds = element.Bounds;
        var layout = new StringBuilder(string.Create(CultureInfo.InvariantCulture,
            $"at {bounds.X:0.#},{bounds.Y:0.#} size {size.Width:0.#}x{size.Height:0.#}"));
        if (element is MeasurableUIComponent measured)
        {
            layout.Append(string.Create(CultureInfo.InvariantCulture,
                $" desired {measured.DesiredSize.Width:0.#}x{measured.DesiredSize.Height:0.#}"));
            if (!measured.IsMeasureValid)
            {
                layout.Append(" measure-invalid");
            }

            if (!measured.IsArrangeValid)
            {
                layout.Append(" arrange-invalid");
            }
        }

        if (element.Visibility != Visibility.Visible)
        {
            layout.Append(' ').Append(element.Visibility);
        }

        if (element.ClipToBounds)
        {
            layout.Append(" clips");
        }

        if (!element.IsHitTestVisible)
        {
            layout.Append(" not-hit-testable");
        }

        return layout.ToString();
    }

    private static string ValueOf(AdamantiumComponent element, AdamantiumProperty property)
    {
        try
        {
            return element.GetValue(property) switch
            {
                null => "null",
                string text => $"\"{text}\"",
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                var value => value.ToString()
            };
        }
        catch (Exception e)
        {
            return $"<{e.GetType().Name}: {e.Message}>";
        }
    }

    private static string Describe(BindingBase binding) => binding switch
    {
        null => null,
        Binding { Path.Path: { } path } => $"Binding {path}",
        _ => binding.GetType().Name
    };

    private static string Describe(object node) => node switch
    {
        null => null,
        IName { Name: { Length: > 0 } name } => $"{node.GetType().Name} #{name}",
        _ => node.GetType().Name
    };
}
