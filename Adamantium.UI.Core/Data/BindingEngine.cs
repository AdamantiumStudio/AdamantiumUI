using System.Runtime.CompilerServices;

namespace Adamantium.UI.Core.Data;

/// <summary>
/// Central registry and entry point for data bindings - the WPF <c>BindingOperations</c> analog. Owns every active
/// <see cref="BindingExpression"/>, keyed by its target element (weakly, so it never keeps elements alive) and its
/// target property, so bindings can be inspected, refreshed or cleared programmatically rather than being buried as
/// private state inside each element.
/// </summary>
public static class BindingEngine
{
    // Weak element keys -> the element's live bindings by target property. Weak so the registry never pins an element.
    private static readonly ConditionalWeakTable<IAdamantiumComponent, Dictionary<AdamantiumProperty, BindingExpressionBase>> _bindings = new();

    private static readonly ConditionalWeakTable<IAdamantiumComponent, Dictionary<(AdamantiumProperty Property, object Owner), BindingExpressionBase>> _owned = new();

    public static BindingExpressionBase SetBinding(IAdamantiumComponent target, AdamantiumProperty targetProperty,
        BindingBase bindingBase)
    {
        var map = _bindings.GetValue(target, static _ => new Dictionary<AdamantiumProperty, BindingExpressionBase>());

        // THE ONE THAT STOOD HERE GOES FIRST. A new binding pushes its value as it is made, and a two-way binding that
        // is still listening to this target reads that push as the target being edited - and writes it into ITS OWN
        // source. Re-pointing a row of targets at another row of sources then drags every old source along: a node's
        // sockets, re-fitted after one of them moved, all ended up called the same thing.
        if (map.TryGetValue(targetProperty, out var existing)) existing.CloseConnection();

        var expression = BindingExpression.CreateBindingExpression(target, targetProperty, bindingBase);
        map[targetProperty] = expression;
        return expression;
    }

    public static BindingExpressionBase SetBinding(IAdamantiumComponent target, string targetProperty,
        BindingBase bindingBase)
        => SetBinding(target, target.GetProperty(targetProperty), bindingBase);

    /// <summary>Registers an already-constructed expression (built by a template) so it is refreshed when the target's
    /// DataContext changes - a DataTemplate's {Binding}s are created before the container's DataContext exists, so they
    /// must re-resolve once it arrives - then establishes it. Producer/target-less expressions are just established.
    /// </summary>
    public static void Register(BindingExpressionBase expression)
    {
        if (expression == null) return;
        if (expression.Target == null || expression.TargetProperty == null)
        {
            expression.EstablishConnection();
            return;
        }

        var map = _bindings.GetValue(expression.Target, static _ => new Dictionary<AdamantiumProperty, BindingExpressionBase>());
        if (map.TryGetValue(expression.TargetProperty, out var existing) && !ReferenceEquals(existing, expression))
            existing.CloseConnection();
        map[expression.TargetProperty] = expression;
        expression.EstablishConnection();
    }

    internal static void RegisterOwned(BindingExpressionBase expression, object owner)
    {
        var map = _owned.GetValue(expression.Target, static _ => new Dictionary<(AdamantiumProperty, object), BindingExpressionBase>());
        var key = (expression.TargetProperty, owner);
        if (map.TryGetValue(key, out var existing) && !ReferenceEquals(existing, expression)) existing.CloseConnection();
        map[key] = expression;
        expression.EstablishConnection();
    }

    internal static void ClearOwned(IAdamantiumComponent target, AdamantiumProperty property, object owner)
    {
        if (_owned.TryGetValue(target, out var map) && map.Remove((property, owner), out var e)) e.CloseConnection();
    }

    internal static bool IsFedByStyle(IAdamantiumComponent target, AdamantiumProperty property)
    {
        if (!_owned.TryGetValue(target, out var map)) return false;

        foreach (var pair in map)
        {
            if (pair.Key.Property == property && pair.Value.OwnerStyle != null) return true;
        }

        return false;
    }

    /// <summary>The element's own live binding on a target property, or null.</summary>
    public static BindingExpressionBase GetBindingExpression(IAdamantiumComponent target, AdamantiumProperty targetProperty)
        => _bindings.TryGetValue(target, out var map) && map.TryGetValue(targetProperty, out var e) ? e : null;

    /// <summary>Every live binding on an element, its styles' and triggers' included.</summary>
    public static IReadOnlyCollection<BindingExpressionBase> GetBindings(IAdamantiumComponent target)
        => [.. Own(target), .. Owned(target)];

    /// <summary>Has this element anything to re-resolve at all? Asked by the inheritance walk before it pays the full
    /// write for a `DataContext` change: three quarters of the elements a container rebind reaches have no binding on
    /// them, and telling them costs slots, priorities and a notification for nothing.</summary>
    public static bool HasBindings(IAdamantiumComponent target)
        => (_bindings.TryGetValue(target, out var map) && map.Count > 0)
           || (_owned.TryGetValue(target, out var owned) && owned.Count > 0);

    /// <summary>Re-resolves and re-applies every binding on an element - e.g. after its DataContext changed.</summary>
    public static void RefreshBindings(IAdamantiumComponent target)
    {
        if (_bindings.TryGetValue(target, out var map))
            foreach (var e in map.Values) e.EstablishConnection();
        if (_owned.TryGetValue(target, out var owned))
            foreach (var e in owned.Values) e.EstablishConnection();
    }

    public static void ClearBinding(IAdamantiumComponent target, AdamantiumProperty targetProperty)
    {
        if (_bindings.TryGetValue(target, out var map) && map.Remove(targetProperty, out var e)) e.CloseConnection();
    }

    public static void ClearBindings(IAdamantiumComponent target)
    {
        foreach (var e in GetBindings(target)) e.CloseConnection();

        if (_bindings.TryGetValue(target, out var map)) map.Clear();
        if (_owned.TryGetValue(target, out var owned)) owned.Clear();
    }

    /// <summary>Closes every binding's source subscription but keeps it registered, for parked containers. The caller must
    /// reopen them with <see cref="RefreshBindings"/>.</summary>
    public static void DeactivateBindings(IAdamantiumComponent target)
    {
        if (_bindings.TryGetValue(target, out var map))
            foreach (var e in map.Values) e.CloseConnection();
        if (_owned.TryGetValue(target, out var owned))
            foreach (var e in owned.Values) e.CloseConnection();
    }

    private static IEnumerable<BindingExpressionBase> Own(IAdamantiumComponent target)
        => _bindings.TryGetValue(target, out var map) ? map.Values : [];

    private static IEnumerable<BindingExpressionBase> Owned(IAdamantiumComponent target)
        => _owned.TryGetValue(target, out var map) ? map.Values : [];
}
