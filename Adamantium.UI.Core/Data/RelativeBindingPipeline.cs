using System;

namespace Adamantium.UI.Core.Data;

/// <summary>
/// The fallback an <c>{Ancestor}</c> with no ancestor to read from still pushes: converter -> TargetNullValue /
/// FallbackValue -> target-type coercion, with the same semantics as <see cref="BindingExpression"/>.
/// </summary>
internal static class RelativeBindingPipeline
{
    /// <summary>Sentinel for "the path could not be resolved" (distinct from a resolved null). It is the ENGINE'S own
    /// unset token, not one of this file's making: a second object meaning the same thing is a second thing to keep in
    /// step, and every binding path has to agree on what "no value" is.</summary>
    internal static readonly object Unset = AdamantiumProperty.UnsetValue;

    /// <summary>The plain binding an <c>{Ancestor}</c> or a <c>{Self}</c> follows its path with, once its source is found:
    /// what it says of the value - fallbacks, format, timing - it says through that one.</summary>
    internal static Binding Following(BindingBase relative, string path, BindingMode mode, UpdateSourceTrigger trigger,
        IValueConverter converter, object converterParameter) => relative.CopiedInto(new Binding(path)
    {
        Mode = mode,
        UpdateSourceTrigger = trigger,
        Converter = converter,
        ConverterParameter = converterParameter,
    });

    /// <summary>Converter -> TargetNullValue/FallbackValue -> coerce. Returns <see cref="Unset"/> when nothing usable can
    /// be produced (the caller then leaves the target at its default rather than clobbering it).</summary>
    internal static object Produce(object raw, IValueConverter converter, object converterParameter, Type targetType,
        object fallback, object targetNullValue)
    {
        object value;
        if (ReferenceEquals(raw, Unset))
        {
            value = fallback;   // path didn't resolve -> WPF FallbackValue semantics
        }
        else
        {
            value = converter != null
                ? converter.Convert(raw, targetType, converterParameter, Localization.Languages.Culture)
                : raw;
        }

        if (value == null) value = targetNullValue ?? fallback;
        // A null that came from a path that DID resolve is a value, and it goes to the target. Only an unresolved path
        // with nothing to fall back on has nothing to say - collapsing the two meant a source could never hand a
        // property back its default, which is what null means for every "unset it and let the theme decide" property.
        // A property that cannot HOLD nothing (a bare value type) is the exception: there is no null to give it back.
        if (value == null)
            return ReferenceEquals(raw, Unset) || !BindingExpressionBase.CanHoldNothing(targetType) ? Unset : null;
        return BindingExpressionBase.TryCoerce(value, targetType, out var coerced) ? coerced : Unset;
    }
}
