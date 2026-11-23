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
    private static readonly ConditionalWeakTable<IAdamantiumComponent, Dictionary<(string Slot, object Token), ThemeResourceExpression>> _applied = new();

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
    /// <see cref="Diagnostics.PropertyTrace"/>. A target outside the tree - a drawing of a dictionary icon - takes the
    /// application's theme current when it connects, and that theme's changes live.</summary>
    public ThemeResourceExpression Apply(IAdamantiumComponent target, string propertyName,
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
        if (map.TryGetValue(slot, out var previous)) previous.Release();   // defensive: re-apply on the same slot
        map[slot] = expression;
        return expression;
    }

    /// <summary>Connects every <c>{ThemeResource}</c> on <paramref name="target"/>, a component outside the tree, to
    /// the theme now in force where it hangs - called as a scope's values reach it.</summary>
    internal static void FollowScope(IAdamantiumComponent target)
    {
        if (!_applied.TryGetValue(target, out var map) || map.Count == 0) return;

        // A snapshot: connecting again can apply a trigger that applies or removes another entry here.
        foreach (var expression in map.Values.ToArray()) expression.FollowScope();
    }

    /// <summary>Disposes the expression a setter/trigger established (closing its theme subscription) and clears the
    /// value it pushed - so leaving a trigger or detaching a style fully undoes a <c>{ThemeResource}</c>.</summary>
    public static void Remove(IAdamantiumComponent target, string propertyName, ValuePriority priority, object token = null)
    {
        if (!_applied.TryGetValue(target, out var map)) return;
        if (!map.Remove((propertyName + "@" + priority, token), out var expression)) return;

        expression.Release();

        // A TOKEN owns one contribution on the trigger stack and nothing else, so it always takes that contribution
        // with it - a survivor on the same slot keeps its own. Standing aside here is what left a color on a part
        // with no owner: two accent triggers share a checkbox's box, and whichever left first refreshed the other and
        // returned, so its own value stayed on the stack for good (an unticked box keeping the accent fill).
        if (priority == ValuePriority.Trigger && token != null)
        {
            target.ClearTriggerValue(target.GetProperty(propertyName), token);
            return;
        }

        if (Style.GetContributingStyle(priority, token) is { } style)
        {
            target.RemoveStyleValue(propertyName, style);
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
        if (context?.TargetObject is IAdamantiumComponent target && !string.IsNullOrEmpty(context.TargetPropertyName))
            Apply(target, context.TargetPropertyName);
        return this;
    }
}
