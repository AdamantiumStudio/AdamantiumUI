using System;
using Adamantium.UI.Core.MarkupExtensions;

namespace Adamantium.UI.Core.Data;

/// <summary><c>{Ancestor TitleBar, Foreground}</c>: binds to a property of the nearest ancestor of a type (is-a), walking
/// the visual tree unless <see cref="Logical"/>. Re-resolves whenever the target re-attaches. A binding like any other:
/// it goes wherever one does - a trigger's condition, a MultiBinding's input.</summary>
public class Ancestor : BindingBase
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

    /// <summary>When a TwoWay write reaches the ancestor, like a {Binding}'s: LostFocus for a field applied once left.</summary>
    public UpdateSourceTrigger UpdateSourceTrigger { get; set; }

    /// <summary>Optional converter applied to the source value (and back on a TwoWay write), like a {Binding} converter.</summary>
    public IValueConverter Converter { get; set; }

    public object ConverterParameter { get; set; }

    public override object Clone() => CopiedInto(new Ancestor
    {
        AncestorType = AncestorType,
        Path = Path,
        Skip = Skip,
        Name = Name,
        Stop = Stop,
        Logical = Logical,
        Mode = Mode,
        UpdateSourceTrigger = UpdateSourceTrigger,
        Converter = Converter,
        ConverterParameter = ConverterParameter,
    });

    public override BindingExpressionBase CreateExpression(IAdamantiumComponent target, AdamantiumProperty targetProperty) =>
        new AncestorBindingExpression(target, targetProperty, this);
}
