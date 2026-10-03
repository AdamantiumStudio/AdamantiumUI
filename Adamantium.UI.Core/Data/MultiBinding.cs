using System.Collections.ObjectModel;

namespace Adamantium.UI.Core.Data;

/// <summary>Combines several nestable <see cref="Bindings"/> into one target value via <see cref="Converter"/>, or
/// <see cref="BindingBase.StringFormat"/> without one. One-way.</summary>
public class MultiBinding : BindingBase
{
   public Collection<BindingBase> Bindings { get; } = new();

   public BindingMode Mode { get; set; }

   public IMultiValueConverter Converter { get; set; }

   public object ConverterParameter { get; set; }

   public override object Clone()
   {
      var clone = new MultiBinding
      {
         Mode = Mode,
         Converter = Converter,
         ConverterParameter = ConverterParameter,
         StringFormat = StringFormat,
         Culture = Culture,
         FallbackValue = FallbackValue,
         TargetNullValue = TargetNullValue,
         IsAsync = IsAsync,
         Delay = Delay,
      };
      foreach (var binding in Bindings)
         clone.Bindings.Add((BindingBase)binding.Clone());
      return clone;
   }

   public override BindingExpressionBase CreateExpression(IAdamantiumComponent target, AdamantiumProperty targetProperty) =>
      new MultiBindingExpression(target, targetProperty, this);
}
