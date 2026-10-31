using Adamantium.UI.Core.MarkupExtensions;

namespace Adamantium.UI.Core.Resources;

public static class ResourceContext
{
    static ResourceContext()
    {
        DiscardedVisuals.Discarded += ReleaseDiscarded;
    }

    public static readonly AdamantiumProperty SourceProperty =
        AdamantiumProperty.RegisterAttached<ResourceLink>("Source", typeof(AdamantiumComponent));

    public static ResourceLink GetSource(AdamantiumComponent element)
    {
        return element.GetValue<ResourceLink>(SourceProperty);
    }
    
    public static void SetSource(AdamantiumComponent element, ResourceLink value)
    {
        var old = GetSource(element);
        element.SetValue(SourceProperty, value);

        // A THEME only records its palette link here; the ThemeManager activates it (AddSource) while the theme is
        // current and removes it when it stops being current. That keeps exactly one palette - the current theme's -
        // live, so declaring 20 themes doesn't register 20 palettes. Every non-theme element registers eagerly, below.
        if (element is ITheme || ReferenceEquals(old, value)
            || old != null && value != null && old.Source == value.Source && old.Scope == value.Scope) return;

        var manager = UIAppContext.Current.ResourceManager;
        var styled = manager.HasScopedStyles;
        if (old?.Source != null)
        {
            manager.RemoveSource(element, old.Source, old.Scope);
        }

        if (value?.Source != null)
        {
            manager.AddSource(element, value.Source, value.Scope);
        }

        RestyleIfStyled(element, styled || manager.HasScopedStyles);
    }

    // The scope the inline Resources below are published into. LOCAL by default - a private, tree-scoped dictionary that
    // is invisible outside this subtree. Set it to Theme on a theme (icons belong to the theme: another theme may declare
    // the same keys) or to Global for an app-wide set, without having to move the entries into a separate linked type.
    public static readonly AdamantiumProperty ScopeProperty =
        AdamantiumProperty.RegisterAttached("Scope", typeof(ResourceScope), typeof(AdamantiumComponent),
            new PropertyMetadata(ResourceScope.Local));

    public static ResourceScope GetScope(AdamantiumComponent element)
    {
        return element.GetValue<ResourceScope>(ScopeProperty);
    }

    public static void SetScope(AdamantiumComponent element, ResourceScope value)
    {
        element.SetValue(ScopeProperty, value);
    }

    // The element's resources: <ResourceLink>s to dictionary types and inline keyed objects, scoped by
    // ResourceContext.Scope (Local by default) unless a link says otherwise.
    public static readonly AdamantiumProperty ResourcesProperty =
        AdamantiumProperty.RegisterAttached<ResourceDictionary>("Resources", typeof(AdamantiumComponent));

    public static ResourceDictionary GetResources(AdamantiumComponent element)
    {
        return element.GetValue<ResourceDictionary>(ResourcesProperty);
    }

    public static void SetResources(AdamantiumComponent element, ResourceDictionary value)
    {
        var old = GetResources(element);
        element.SetValue(ResourcesProperty, value);

        // A THEME publishes nothing until it is the current one - the same rule its palette link follows, so declaring
        // 20 themes doesn't put 20 icon sets into the Theme scope at once. The ThemeManager activates it.
        if (element is ITheme || ReferenceEquals(old, value)) return;

        var manager = UIAppContext.Current.ResourceManager;
        var styled = manager.HasScopedStyles;
        var scope = GetScope(element);
        if (old != null)
        {
            manager.RemoveSource(element, old, scope);
        }

        if (value != null)
        {
            manager.AddSource(element, value, scope);
        }

        RestyleIfStyled(element, styled || manager.HasScopedStyles);
    }

    private static void RestyleIfStyled(AdamantiumComponent element, bool styled)
    {
        if (styled)
        {
            (element as FundamentalUIComponent)?.RestyleForScopes();
        }
    }

    private static void ReleaseDiscarded(ReadOnlySpan<IFundamentalUIComponent> gone)
    {
        var manager = UIAppContext.Current?.ResourceManager;
        if (manager == null)
        {
            return;
        }

        foreach (var component in gone)
        {
            if (component is not AdamantiumComponent element || element is ITheme)
            {
                continue;
            }

            if (GetSource(element) == null && GetResources(element) == null)
            {
                continue;
            }

            manager.ForgetGlobalSourceOwner(element);
            manager.RemoveSources(element);
        }
    }

    internal static void RegisterResources(AdamantiumComponent element, ResourceDictionary resources) =>
        UIAppContext.Current.ResourceManager.AddSource(element, resources, GetScope(element));
}