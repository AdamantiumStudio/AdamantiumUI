using System;
using System.Globalization;
using Adamantium.UI.Core.Diagnostics;
using Adamantium.UI.Core.Localization;
using Adamantium.UI.Core.Resources;

namespace Adamantium.UI.Core.Data;

public abstract class BindingExpressionBase
{
   public virtual bool HasError { get; protected set; }

   /// <summary>Whether the binding found its source and path property, as opposed to reading null from it. True for
   /// bindings without a path.</summary>
   public virtual bool IsResolved => true;

   public virtual bool HasValidationError { get; protected set; }

   public bool IsDirty { get; set; }

   public BindingBase BindingBase { get; internal set;}

   public BindingStatus Status { get; internal set; }

   internal string Failure { get; private set; }

   internal void Fail(string message)
   {
      Status = BindingStatus.PathError;
      Failure = message;
      BindingTrace.Suspect(this, message);
   }

   internal virtual void Retry()
   {
      EstablishConnection();
   }

   /// <summary>What the binding writes to. Any component with the property system, NOT only a tree element: a Transform
   /// is an AdamantiumComponent that carries animatable properties but sits outside the logical tree, and refusing to
   /// bind it made <c>&lt;Transform ScaleX="{Binding Zoom}"/&gt;</c> - the obvious markup - impossible. It reaches a
   /// DataContext through its InheritanceParent, which is what <see cref="DataContextSource"/> walks.</summary>
   public IAdamantiumComponent Target { get; set; }

   /// <summary>The nearest tree element at or above <see cref="Target"/> - the thing that actually has a DataContext to
   /// bind against, and the anchor for ElementName/ancestor lookups.</summary>
   public IFundamentalUIComponent DataContextSource => NearestElement(Target);

   internal static IFundamentalUIComponent NearestElement(IAdamantiumComponent component)
   {
      for (var node = component; node != null; node = node.InheritanceParent)
         if (node is IFundamentalUIComponent element) return element;

      return null;
   }

   public AdamantiumProperty TargetProperty { get; set; }

   internal ValuePriority Priority { get; set; } = ValuePriority.Binding;

   internal Style OwnerStyle { get; set; }

   internal object TriggerToken { get; set; }

   protected void WriteTarget(object value)
   {
      if (OwnerStyle != null)
      {
         Target.SetStyleValue(TargetProperty, value, OwnerStyle);
      }
      else if (TriggerToken != null)
      {
         Target.SetTriggerValue(TargetProperty, value, TriggerToken);
      }
      else
      {
         Target.SetValue(TargetProperty, value, Priority);
      }
   }

   protected T WritingHere<T>(T inner) where T : BindingExpressionBase
   {
      inner.Priority = Priority;
      inner.OwnerStyle = OwnerStyle;
      inner.TriggerToken = TriggerToken;
      return inner;
   }

   /// <summary>Whether the source property carries a value WRITTEN INTO IT, as against one arriving from a style, a
   /// trigger or the type's own default. False where the expression cannot tell, which is the safe answer: a panel
   /// then offers no undo rather than an undo that takes away something nobody wrote.</summary>
   public virtual bool IsSourceEdited => false;

   /// <summary>Drops that written value, so whatever the object would hold without it comes back - the theme's brush,
   /// the style's size, the property's own default. False where there is nothing to drop.</summary>
   public virtual bool ResetSource() => false;

   /// <summary>What the SOURCE will take, where the expression has resolved a property to write back to. The DECLARED
   /// type and not the type of what is in it: a property typed <c>Brush</c> holding a solid color takes a picture
   /// just as well, and anything deciding what fits by asking the value it is replacing would refuse every kind but
   /// the one already there. Null where the expression has nothing to write back to.</summary>
   public virtual Type SourceType => null;

   // The value this expression currently produces (after its own converter, before any target-type coercion). It
   // matters only when the expression is a CHILD of a MultiBinding: its value feeds the parent's converter instead
   // of driving a target property. A top-level expression (TargetProperty != null) pushes straight to the target.
   public object ProducedValue { get; protected set; }

   // Raised when ProducedValue changes so a parent MultiBindingExpression can recombine. This is what makes
   // multibinding-inside-multibinding work: expressions nest as producers and bubble changes upward.
   public event Action<BindingExpressionBase> ValueChanged;

   protected void RaiseValueChanged() => ValueChanged?.Invoke(this);

   public virtual void UpdateSource()
   { }

   public virtual void UpdateTarget()
   { }

   // F2: queue this expression for the once-per-frame coalesced flush instead of pushing synchronously - unless the
   // binding opted into immediate application (a side-effect binding). Used for RUNTIME source changes; the initial
   // connect push stays synchronous.
   protected void ScheduleUpdate()
   {
      // Only a TOP-LEVEL binding (one that writes to a UI target) is batched. A producer (TargetProperty == null, a
      // MultiBinding child) feeds its parent synchronously - it's combinator plumbing, not a target write, and is also
      // exercised in isolation with no frame to flush it. IsImmediate opts a side-effect binding back to synchronous.
      if (TargetProperty == null || BindingBase?.IsImmediate == true) ApplyPending();
      else BindingUpdateQueue.Enqueue(this);
   }

   // F2: apply the pending (coalesced) update - reads the CURRENT source value and pushes it to the target. Called by
   // the per-frame BindingUpdateQueue flush; reading the latest value is what makes N source changes collapse to one.
   internal virtual void ApplyPending() => UpdateTarget();

   internal CultureInfo FormatCulture => Languages.CultureFor(BindingBase?.Culture);

   internal virtual void OnLanguageChanged() => ScheduleUpdate();

   public abstract void EstablishConnection();
   public abstract void CloseConnection();

   // TEMP (leak hunt): source-side subscriptions taken and given up. The SOURCE is the long-lived end (a view model
   // outlives every view built against it), so a hook that is never given up holds its expression - and through it the
   // element the expression targets.
   public static long SourceHooks, SourceUnhooks;

   // Lenient coercion: keeps the raw value when it can't be made to fit (used writing back to source).
   internal static object Coerce(object value, Type targetType)
      => TryCoerce(value, targetType, out var result) ? result : value;

   // Strict: false (result=null) when the value can't be made to fit, so the caller skips the assignment instead of
   // pushing something that would throw.
   /// <summary>Whether a property of this type has a null to be put back into it. A reference type and a Nullable&lt;T&gt;
   /// do; a bare value type does not, and handing one a null leaves a slot that reads as (double)null.</summary>
   internal static bool CanHoldNothing(Type type)
      => type is not { IsValueType: true } || Nullable.GetUnderlyingType(type) != null;

   internal static bool TryCoerce(object value, Type targetType, out object result, CultureInfo culture = null)
   {
      result = value;
      if (value == null || targetType == null || targetType.IsInstanceOfType(value)) return true;
      culture ??= Languages.Culture;
      if (targetType == typeof(string))
      {
         result = value is IFormattable formattable ? formattable.ToString(null, culture) : value.ToString();
         return true;
      }

      try
      {
         result = Convert.ChangeType(value, Nullable.GetUnderlyingType(targetType) ?? targetType, culture);
         return true;
      }
      catch
      {
         // What Convert.ChangeType cannot place still converts through the engine's TypeParser - the SAME conversion the
         // markup compiler runs, so a bound value and an authored attribute land alike (a string -> Brush/Geometry, a
         // double -> GridLength).
         var parsed = TypeCastFactory.CastFromString(value, targetType);
         if (parsed != AdamantiumProperty.UnsetValue)
         {
            result = parsed;
            return true;
         }

         result = null;
         return false;
      }
   }
}
