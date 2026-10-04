using System.Runtime.CompilerServices;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.MarkupExtensions;

namespace Adamantium.UI.Core.Resources;

/// <summary><c>{ThemeResource Key}</c>: a live reference to a property of the active theme (accent, focus colors) that
/// follows runtime changes, unlike the one-shot <see cref="ResourceReference"/>.</summary>
public class ThemeResource : MarkupExtension
{
    // Live expressions created by Apply, per target, keyed by "property@priority". Lets a setter/trigger dispose the
    // exact expression it established (Remove) without disturbing a same-property expression at another priority - e.g.
    // a template's base {ThemeResource} sitting under a hover trigger's override. Weak keys: never pins a control.
    private static readonly ConditionalWeakTable<IFundamentalUIComponent, Dictionary<(string Slot, object Token), ThemeResourceExpression>> _applied = new();

    public ThemeResource()
    {
    }

    public ThemeResource(string key)
    {
        Key = key;
    }

    /// <summary>The name of the theme property to track (e.g. AccentFillColorDefault).</summary>
    [DefaultProperty]
    public string Key { get; set; }

    /// <summary>Connects a live <see cref="ThemeResourceExpression"/> from the theme's <see cref="Key"/> property to
    /// <paramref name="propertyName"/> on <paramref name="target"/>. Used by codegen, setters and the runtime loader.
    /// A name the target has no property for connects nothing, and is reported through
    /// <see cref="Diagnostics.PropertyTrace"/>.</summary>
    public ThemeResourceExpression Apply(IFundamentalUIComponent target, string propertyName,
        ValuePriority priority = ValuePriority.Template, object token = null)
    {
        var property = target.GetProperty(propertyName);
        if (property == null)
        {
            Diagnostics.PropertyTrace.Log(
                $"{target.GetType().Name} has no property {propertyName} for {{ThemeResource {Key}}}; nothing was connected.");
            return null;
        }

        var expression = new ThemeResourceExpression(target, property, Key, priority, token);
        expression.EstablishConnection();

        var map = _applied.GetValue(target, static _ => new Dictionary<(string, object), ThemeResourceExpression>());
        // Keyed per token so two trigger {ThemeResource}s on the same part property (a checkbox's accent under its
        // checked+hover accent-secondary) each keep their own live connection and stack, instead of one closing the other.
        var slot = (propertyName + "@" + priority, token);
        if (map.TryGetValue(slot, out var previous)) previous.CloseConnection();   // defensive: re-apply on the same slot
        map[slot] = expression;
        return expression;
    }

    /// <summary>Disposes the expression a setter/trigger established (closing its theme subscription) and clears the
    /// value it pushed - so leaving a trigger or detaching a style fully undoes a <c>{ThemeResource}</c>.</summary>
    public static void Remove(IFundamentalUIComponent target, string propertyName, ValuePriority priority, object token = null)
    {
        if (!_applied.TryGetValue(target, out var map)) return;
        if (!map.Remove((propertyName + "@" + priority, token), out var expression)) return;

        expression.CloseConnection();

        // A TOKEN owns one contribution on the trigger stack and nothing else, so it always takes that contribution
        // with it - a survivor on the same slot keeps its own. Standing aside here is what left a color on a part
        // with no owner: two accent triggers share a checkbox's box, and whichever left first refreshed the other and
        // returned, so its own value stayed on the stack for good (an unticked box keeping the accent fill).
        if (priority == ValuePriority.Trigger && token != null)
        {
            target.ClearTriggerValue(target.GetProperty(propertyName), token);
            return;
        }

        // The token-LESS form clears the whole priority slot, and that one must stand aside: while a theme swap has
        // both styles attached, two of them own this property at this priority, and the one that leaves would take the
        // other's value with it.
        var slot = propertyName + "@" + priority;
        foreach (var pair in map)
        {
            if (pair.Key.Slot != slot) continue;

            pair.Value.UpdateTarget();
            return;
        }

        target.ClearValue(propertyName, priority);
    }

    public override object ProvideObject(MarkupContext context)
    {
        if (context?.TargetObject is IFundamentalUIComponent target && !string.IsNullOrEmpty(context.TargetPropertyName))
            Apply(target, context.TargetPropertyName);
        return this;
    }
}
