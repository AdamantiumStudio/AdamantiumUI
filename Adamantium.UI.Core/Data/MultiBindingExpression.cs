using System.Collections.Generic;
using Adamantium.UI.Core.Localization;

namespace Adamantium.UI.Core.Data;

/// <summary>A live <see cref="MultiBinding"/>: combines its producer children's values through the converter and pushes
/// the result to the target, or publishes it when nested in another MultiBinding.</summary>
public class MultiBindingExpression : BindingExpressionBase
{
   public MultiBinding MultiBinding { get; }
   public BindingMode Mode { get; }

   private readonly List<BindingExpressionBase> _children = new();
   private bool _suspendRefresh;
   private bool IsProducer => TargetProperty == null;

   public MultiBindingExpression(IAdamantiumComponent target, AdamantiumProperty targetProperty, BindingBase bindingBase)
   {
      Target = target;
      TargetProperty = targetProperty;
      BindingBase = bindingBase;
      MultiBinding = (MultiBinding)bindingBase;
      Mode = MultiBinding.Mode;
   }

   public override void EstablishConnection()
   {
      CloseConnection();
      if (!IsProducer)
      {
         Languages.Follow(this);
      }

      // Establish children with combine suspended: each child publishes its initial value as it connects, but we
      // must not combine until ALL children exist (a partial value array breaks a fixed-arity StringFormat/converter).
      _suspendRefresh = true;
      foreach (var childBinding in MultiBinding.Bindings)
      {
         var child = CreateChild(childBinding);
         if (child == null) continue;
         child.ValueChanged += OnChildValueChanged;
         _children.Add(child);
         child.EstablishConnection();
      }
      _suspendRefresh = false;
      Refresh();
   }

   // Children share our Target (so they resolve against the same DataContext) but have NO target property — their
   // values feed our converter. A child may be any kind of binding, a MultiBinding too, which is what enables nesting.
   private BindingExpressionBase CreateChild(BindingBase childBinding) => childBinding?.CreateExpression(Target, null);

   private void OnChildValueChanged(BindingExpressionBase child)
   {
      if (!_suspendRefresh) Refresh();
   }

   private void Refresh()
   {
      var value = Combine();
      if (!IsProducer && Target != null)
      {
         WriteTarget(value);
      }
      else
      {
         ProducedValue = value;
         RaiseValueChanged();
      }
   }

   private object Combine()
   {
      var values = new object[_children.Count];
      for (var i = 0; i < _children.Count; i++)
         values[i] = _children[i].ProducedValue;

      var targetType = TargetProperty?.PropertyType ?? typeof(object);
      object result;
      if (MultiBinding.Converter != null)
         result = MultiBinding.Converter.Convert(values, targetType, MultiBinding.ConverterParameter, FormatCulture);
      else if (!string.IsNullOrEmpty(MultiBinding.StringFormat))
         result = string.Format(FormatCulture, MultiBinding.StringFormat, values);
      else
         // No converter and no StringFormat: a multi-binding has no single natural value — hand back the array.
         return values;

      // Converter produced no value: fall back (TargetNullValue first, then FallbackValue) - WPF semantics.
      return result ?? MultiBinding.TargetNullValue ?? MultiBinding.FallbackValue;
   }

   internal override void OnLanguageChanged() => Refresh();

   public override void CloseConnection()
   {
      Languages.Unfollow(this);
      foreach (var child in _children)
      {
         child.ValueChanged -= OnChildValueChanged;
         child.CloseConnection();
      }
      _children.Clear();
   }
}
