using Adamantium.UI.Core.MarkupExtensions;

namespace Adamantium.UI.Core.Data;

public abstract class BindingBase: MarkupExtension, ICloneable
{
   public uint Delay { get; set; }

   public object FallbackValue { get; set; }

   public string StringFormat { get; set; }

   /// <summary>How this binding writes numbers and dates: "Invariant" or a culture name such as "de-DE". Unset follows
   /// the application's language; set it only to depart from that language's rules.</summary>
   public string Culture { get; set; }

   public object TargetNullValue { get; set; }

   public bool IsAsync { get; set; }

   // F2 binding-storm batching: by default a runtime source change is coalesced and applied to the target once per
   // frame (the target sees the frame-final value, not every intermediate). Set true to opt a side-effect binding back
   // into synchronous application (applied the instant the source changes). The initial connect push is always synchronous.
   public bool IsImmediate { get; set; }

   // When the markup context gives us the target element + property, establish a live binding through the engine
   // (which dispatches to a BindingExpression or MultiBindingExpression). Without a usable target we just hand back
   // the binding object itself.
   public override object ProvideObject(MarkupContext context)
   {
      if (context?.TargetObject is IAdamantiumComponent target && !string.IsNullOrEmpty(context.TargetPropertyName))
         return BindingEngine.SetBinding(target, context.TargetPropertyName, this);
      return this;
   }

   public abstract object Clone();

   /// <summary>The live expression this kind of binding is: not yet connected, writing to <paramref name="targetProperty"/>
   /// of <paramref name="target"/>, or producing a value for a parent binding when the property is null.</summary>
   public abstract BindingExpressionBase CreateExpression(IAdamantiumComponent target, AdamantiumProperty targetProperty);
}
