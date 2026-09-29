using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Adamantium.UI.Core.Resources;

public static class ResourceResolver
{
    // Keyed weakly: remembering an ask must not keep the brush - or the element it will later be asked about - alive.
    private static readonly ConditionalWeakTable<IAdamantiumComponent, List<(string Property, string Key)>> _pending = new();

    public static T Resolve<T>(string literalName)
    {
        var currentTheme = UIAppContext.Current.ThemeManager.CurrentTheme;

        if (currentTheme == null)
        {
            throw new ResourceNotFoundException($"Current theme not found. Please, check correctness of theme initialization");
        }

        var resource = currentTheme.GetResource(literalName);

        if (resource == null)
        {
            throw new ResourceNotFoundException(
                $"Resource {literalName} is not found for theme: {currentTheme.Name}");
        }

        return (T)resource;
    }

    // A {ResourceReference} on a plain markup object - a template selector, a converter, anything the author writes as an
    // element but that is not part of the property system. There is no property store to defer into and no place in the
    // tree to be scoped from, so the key is resolved AT ONCE and flat: the Local scope first (the dictionary that
    // declared it is already registered by the time the object is built), then Theme, then Global.
    public static object ResolveNow(string key)
    {
        var resourceManager = UIAppContext.Current.ResourceManager;

        return resourceManager.FindResourceInScope(key, ResourceScope.Local) ?? resourceManager.FindResource(key);
    }

    /// <summary>Applies a <c>{ResourceReference}</c> written directly on any component's property: resolves Theme/Global
    /// now and, for visual-tree targets, re-resolves tree-scoped on attach.</summary>
    /// <param name="priority">The priority a literal written there would get: Template inside a ControlTemplate, so
    /// triggers can still change it; Local on an element.</param>
    public static void SetDeferred(IAdamantiumComponent target, string property, string key,
        ValuePriority priority = ValuePriority.Local)
    {
        var resourceManager = UIAppContext.Current?.ResourceManager;

        var baseline = resourceManager?.FindResource(key);
        if (baseline != null)
            target.SetValue(property, baseline, priority);

        if (target is IUIComponent visual)
        {
            // Remembered as well as re-resolved on attach. Attaching is not the only thing that changes the answer:
            // an element inside a theme SCOPE resolves against that scope's theme, and the scope can be switched while
            // the element sits still - a preview pane changing its variant. Without the record there would be nothing
            // to ask again with, and the pane would keep the colors it happened to attach with.
            _pending.GetValue(target, static _ => []).Add((property, key));

            visual.AttachedToVisualTreeEvent += (_, _) =>
            {
                var scoped = resourceManager.FindResource(visual, key);
                if (scoped != null)
                    visual.SetValue(property, scoped, priority);
            };
            return;
        }

        // A NON-VISUAL target - a brush, a gradient stop - takes no part in the visual tree, so a resource declared on a
        // VIEW was simply never found and the brush painted nothing, silently. Remember the ask instead: the target
        // reaches a tree later, when an element takes it for one of its properties (see Resolve).
        _pending.GetValue(target, static _ => []).Add((property, key));
    }

    /// <summary>Whether this target is still waiting on a resource that only a tree can answer.</summary>
    public static bool HasPending(IAdamantiumComponent target) => _pending.TryGetValue(target, out _);

    /// <summary>Re-resolves every attribute <c>{ResourceReference}</c> in the subtree after its theme scope changed; styles
    /// are handled by re-theming. A walk, for a rare explicit act.</summary>
    public static void ReResolveSubtree(IUIComponent root)
    {
        if (root == null) return;

        ResolveThrough(root, root);
        foreach (var child in root.VisualChildren) ReResolveSubtree(child);
    }

    /// <summary>Answer a non-visual target's deferred resources from <paramref name="anchor"/>'s position in the tree.
    /// Called when the target finally has one. Entries are kept, not consumed: a theme swap re-resolves through the same
    /// anchor, and the ask is what stays true, not the answer.</summary>
    public static void Resolve(IAdamantiumComponent target, IUIComponent anchor)
    {
        if (anchor == null || !_pending.TryGetValue(target, out _))
        {
            return;
        }

        ResolveThrough(target, anchor);

        // And AGAIN when the anchor enters the tree: markup assigns a brush to its element BEFORE that element is added
        // to its parent, so the walk above starts from an element that has no ancestors yet.
        anchor.AttachedToVisualTreeEvent += (_, _) => ResolveThrough(target, anchor);
    }

    private static void ResolveThrough(IAdamantiumComponent target, IUIComponent anchor)
    {
        if (!_pending.TryGetValue(target, out var asks))
        {
            return;
        }

        var resourceManager = UIAppContext.Current?.ResourceManager;
        if (resourceManager == null)
        {
            return;
        }

        var resolvedAny = false;
        foreach (var ask in asks)
        {
            var scoped = resourceManager.FindResource(anchor, ask.Key);
            if (scoped == null)
            {
                continue;
            }

            target.SetValue(ask.Property, scoped);
            resolvedAny = true;
        }

        // RE-RECORD, not a paint re-bake: a texture is asked for while the unit is routed into a batch, so an ImageBrush
        // that got its source this way would otherwise stay empty for ever - the value there, nobody looking again.
        if (resolvedAny)
        {
            anchor.InvalidateRender(false);
        }
    }
}
