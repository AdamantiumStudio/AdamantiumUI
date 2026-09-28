using System.Runtime.CompilerServices;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.MarkupExtensions;

namespace Adamantium.UI.Core.Resources;

/// <summary><c>{ObservableResource Key}</c>: a live, tree-scoped reference to a keyed resource that follows theme swaps
/// and dictionary changes, unlike the one-shot <see cref="ResourceReference"/>.</summary>
public class ObservableResource : MarkupExtension
{
    // Live expressions created by Apply, per target, keyed by "property@priority" + token - so a setter/trigger disposes
    // the exact expression it established without disturbing a same-property expression at another priority/token (a
    // template base under a hover-trigger override). Weak keys: never pins a control. Mirrors ThemeResource.
    private static readonly ConditionalWeakTable<IAdamantiumComponent, Dictionary<(string Slot, object Token), ObservableResourceExpression>> _applied = new();

    public ObservableResource()
    {
    }

    public ObservableResource(string key)
    {
        Key = key;
    }

    /// <summary>The resource key to resolve and track.</summary>
    [DefaultProperty]
    public string Key { get; set; }

    /// <summary>Connects a live <see cref="ObservableResourceExpression"/> from the keyed resource to
    /// <paramref name="propertyName"/> on <paramref name="target"/>. Used by codegen, setters and triggers.</summary>
    /// <remarks>Works on non-tree targets such as gradient stops, which resolve against Theme and Global.</remarks>
    public ObservableResourceExpression Apply(IAdamantiumComponent target, string propertyName,
        ValuePriority priority = ValuePriority.Template, object token = null)
    {
        var expression = new ObservableResourceExpression(target, target.GetProperty(propertyName), Key, priority, token);
        expression.EstablishConnection();

        var map = _applied.GetValue(target, static _ => new Dictionary<(string, object), ObservableResourceExpression>());
        var slot = (propertyName + "@" + priority, token);
        if (map.TryGetValue(slot, out var previous)) previous.CloseConnection();   // defensive: re-apply on the same slot
        map[slot] = expression;
        return expression;
    }

    /// <summary>Disposes the expression a setter/trigger established (closing its resource subscription) and clears the
    /// value it pushed - so leaving a trigger or detaching a style fully undoes an <c>{ObservableResource}</c>.</summary>
    public static void Remove(IAdamantiumComponent target, string propertyName, ValuePriority priority, object token = null)
    {
        if (!_applied.TryGetValue(target, out var map)) return;
        if (!map.Remove((propertyName + "@" + priority, token), out var expression)) return;

        expression.CloseConnection();

        // A TOKEN owns one contribution on the trigger stack and nothing else, so it always takes that contribution
        // with it - a survivor on the same slot keeps its own. See ThemeResource.Remove: standing aside here is what
        // left an unticked checkbox wearing the accent fill.
        if (priority == ValuePriority.Trigger && token != null)
        {
            target.ClearTriggerValue(target.GetProperty(propertyName), token);
            return;
        }

        // Keyed by owner token: during a swap the outgoing style must not close the connection the incoming one just
        // made.
        var slot = propertyName + "@" + priority;
        foreach (var pair in map)
        {
            if (pair.Key.Slot != slot) continue;

            pair.Value.UpdateTarget();   // the surviving owner re-states its value; nothing is cleared
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
