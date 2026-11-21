using Adamantium.UI.Core.MarkupExtensions;

namespace Adamantium.UI.Core.Data;

/// <summary>
/// Terse self binding: <c>{Self Background}</c> binds a target property to ANOTHER property on the SAME element (the
/// WPF <c>{Binding RelativeSource={RelativeSource Self}}</c>). The single positional arg is the source property path.
/// A binding like any other: it goes wherever one does.
/// </summary>
public class Self : BindingBase
{
    [DefaultProperty]
    public string Path { get; set; }

    public BindingMode Mode { get; set; } = BindingMode.OneWay;

    /// <summary>When a TwoWay write reaches the source, like a {Binding}'s.</summary>
    public UpdateSourceTrigger UpdateSourceTrigger { get; set; }

    public IValueConverter Converter { get; set; }

    public object ConverterParameter { get; set; }

    public override object Clone() => CopiedInto(new Self
    {
        Path = Path,
        Mode = Mode,
        UpdateSourceTrigger = UpdateSourceTrigger,
        Converter = Converter,
        ConverterParameter = ConverterParameter,
    });

    public override BindingExpressionBase CreateExpression(IAdamantiumComponent target, AdamantiumProperty targetProperty) =>
        new SelfBindingExpression(target, targetProperty, this);
}
