using System;
using Adamantium.UI.Core.MarkupExtensions;

namespace Adamantium.UI.Core.Data;

/// <summary><c>{Ancestor TitleBar, Foreground}</c>: binds to a property of the nearest ancestor of a type (is-a), walking
/// the visual tree unless <see cref="Logical"/>. Re-resolves whenever the target re-attaches.</summary>
public class Ancestor : MarkupExtension
{
    /// <summary>The ancestor type to search for (first positional). is-a: a base type / interface matches subtypes.</summary>
    [DefaultProperty]
    public Type AncestorType { get; set; }

    /// <summary>The property to read off the resolved ancestor (second positional), e.g. <c>Foreground</c>.</summary>
    public string Path { get; set; }

    /// <summary>Skip this many matching ancestors before binding (0 = nearest). Clearer than WPF's 1-based AncestorLevel.</summary>
    public int Skip { get; set; }

    /// <summary>Optional: only match an ancestor whose x:Name equals this.</summary>
    public string Name { get; set; }

    /// <summary>Optional search boundary: stop (and resolve to nothing) if an ancestor of this type is reached first.</summary>
    public Type Stop { get; set; }

    /// <summary>Walk the LOGICAL tree instead of the visual one. Default false (visual).</summary>
    public bool Logical { get; set; }

    public BindingMode Mode { get; set; } = BindingMode.OneWay;

    /// <summary>Optional converter applied to the source value (and back on a TwoWay write), like a {Binding} converter.</summary>
    public IValueConverter Converter { get; set; }

    public object ConverterParameter { get; set; }

    /// <summary>Value used when the ancestor / path can't be resolved (WPF FallbackValue).</summary>
    public object FallbackValue { get; set; }

    /// <summary>Value used when the resolved value is null (falls back to <see cref="FallbackValue"/> if unset).</summary>
    public object TargetNullValue { get; set; }

    /// <summary>Creates the live expression on <paramref name="target"/>'s <paramref name="propertyName"/> and registers
    /// it so it re-establishes on tree changes. Mirrors <c>ThemeResource.Apply</c> / the codegen emission for bindings.</summary>
    public AncestorBindingExpression Apply(IAdamantiumComponent target, string propertyName,
        ValuePriority priority = ValuePriority.Binding)
    {
        var expression = CreateExpression(target, target.GetProperty(propertyName));
        expression.Priority = priority;
        BindingEngine.Register(expression);
        return expression;
    }

    internal AncestorBindingExpression CreateExpression(IAdamantiumComponent target, AdamantiumProperty property)
        => new(target, property, this);

    public override object ProvideObject(MarkupContext context)
    {
        if (context?.TargetObject is IAdamantiumComponent target && !string.IsNullOrEmpty(context.TargetPropertyName))
            Apply(target, context.TargetPropertyName);
        return this;
    }
}
