using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Core.Resources;

/// <summary>The theme in force at a place in the tree: attached, inherited <c>ThemeContext.Theme</c> and
/// <c>ThemeContext.Variant</c>, e.g. <c>&lt;Border ThemeContext.Variant="Dark"&gt;</c>.</summary>
/// <remarks>A scope replaces the application's theme for its subtree rather than layering over it.</remarks>
public static class ThemeContext
{
    /// <summary>The theme this element and everything under it wears. Unset: whatever the nearest ancestor that names
    /// one says, and failing that the application's current theme.</summary>
    public static readonly AdamantiumProperty ThemeProperty = AdamantiumProperty.RegisterAttached(
        "Theme", typeof(ITheme), typeof(AdamantiumComponent),
        new PropertyMetadata(null, PropertyMetadataOptions.Inherits, OnScopeChanged));

    /// <summary>Which VARIANT of that theme - <c>Light</c>, <c>Dark</c>, <c>System</c>, or whatever else the theme
    /// declares. Unset means "inherit"; <see cref="ThemeVariant.System"/> means "stop inheriting and follow the OS",
    /// which is a different thing and has to be, or it could never be switched on inside a subtree that names a
    /// variant of its own.</summary>
    public static readonly AdamantiumProperty VariantProperty = AdamantiumProperty.RegisterAttached(
        "Variant", typeof(ThemeVariant), typeof(AdamantiumComponent),
        new PropertyMetadata(default(ThemeVariant), PropertyMetadataOptions.Inherits, OnScopeChanged));

    public static ITheme GetTheme(AdamantiumComponent element) => element?.GetValue(ThemeProperty) as ITheme;

    public static void SetTheme(AdamantiumComponent element, ITheme value) => element?.SetValue(ThemeProperty, value);

    public static ThemeVariant GetVariant(AdamantiumComponent element) =>
        element?.GetValue(VariantProperty) is ThemeVariant variant ? variant : default;

    public static void SetVariant(AdamantiumComponent element, ThemeVariant value) =>
        element?.SetValue(VariantProperty, value);

    // Elements that explicitly asked to FOLLOW THE SYSTEM. When the OS appearance flips, the theme they resolve to
    // becomes a different sibling, so their styles - resolved from the old one - have to be re-applied. Only these:
    // everything else is unaffected, and re-styling whole windows for a scope nobody declared would be the expensive
    // cascade again. Held weakly, so asking to follow the system never keeps an element alive.
    private static readonly System.Collections.Generic.List<System.WeakReference<FundamentalUIComponent>> Followers = new();

    static ThemeContext()
    {
        SystemAppearance.Changed += (_, _) => RestyleSystemFollowers();
    }

    private static void RestyleSystemFollowers()
    {
        lock (Followers)
        {
            for (var i = Followers.Count - 1; i >= 0; i--)
            {
                if (Followers[i].TryGetTarget(out var element)) element.InvalidateStyles();
                else Followers.RemoveAt(i);
            }
        }
    }

    // How many elements ever got a scope; zero lets every resource lookup skip the inherited-property reads.
    private static int _scopeCount;

    // Whose scope answers for a component: itself, or for a template part without its own, its templated parent, since
    // parts may not inherit the scope.
    private static AdamantiumComponent ScopeAnchor(IFundamentalUIComponent component)
    {
        var element = component as AdamantiumComponent;

        for (var i = 0; i < 8 && element != null; i++)
        {
            if (element.GetValue(ThemeProperty) != null) return element;
            if (GetVariant(element) is { IsUnspecified: false }) return element;
            if ((element as IFundamentalUIComponent)?.TemplatedParent is not AdamantiumComponent host) break;

            element = host;
        }

        return component as AdamantiumComponent;
    }

    /// <summary>The theme, variant included (a sibling via <see cref="Theme.SiblingForVariant"/>), that
    /// <paramref name="component"/> is styled and resolved against.</summary>
    public static ITheme For(IFundamentalUIComponent component)
    {
        if (_scopeCount == 0) return UIAppContext.Current?.ThemeManager?.CurrentTheme;

        var element = ScopeAnchor(component);
        var theme = (element?.GetValue(ThemeProperty) as ITheme)
                    ?? UIAppContext.Current?.ThemeManager?.CurrentTheme;

        if (theme is not Theme concrete) return theme;

        var variant = GetVariant(element);
        if (variant.FollowsSystem) variant = concrete.ResolveSystemVariant(SystemAppearance.PrefersDark);

        return variant.IsUnspecified ? concrete : concrete.SiblingForVariant(variant);
    }

    // A scope changing is a theme swap for its subtree, and is put right the same way one is: re-apply styles from the
    // root of the scope down. The inherited value has already reached the subtree by the time this runs - the value
    // system propagates before it calls back - so everything below now answers with the new theme.
    private static void OnScopeChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        if (component is not FundamentalUIComponent element) return;

        // A scope now exists, so the fast path in For() has to stop being taken. Counted rather than a bool because the
        // count only ever grows - a scope removed is still a tree that once had one, and getting that wrong would mean
        // silently answering with the application's theme for a subtree that has its own.
        if (e.NewValue != null) System.Threading.Interlocked.Increment(ref _scopeCount);

        // An element that asks to follow the OS has to be found again when the OS changes its mind, and the only
        // moment it can be recorded is the one where it says so.
        if (e.NewValue is ThemeVariant { FollowsSystem: true })
        {
            lock (Followers) Followers.Add(new System.WeakReference<FundamentalUIComponent>(element));
        }

        element.InvalidateStyles();

        // ...and the references written straight onto attributes, which no re-theme reaches: they were resolved once,
        // when the element attached, so without this a scope could be SET before anything was shown but never SWITCHED.
        if (element is IUIComponent visual) ResourceResolver.ReResolveSubtree(visual);

        // ...and live {ObservableResource}s, which re-resolve only on this app-wide event; outside the scope they get the
        // same answer.
        UIAppContext.Current?.ResourceManager?.NotifyResourcesChanged();

        // ...and again when the subtree enters the tree, since parts resolved during the build could not see the scope yet.
        if (element is IUIComponent tracked) HookScopeAttach(tracked);
    }

    private static void HookScopeAttach(IUIComponent element)
    {
        element.AttachedToVisualTreeEvent -= OnScopeAttached;
        element.AttachedToVisualTreeEvent += OnScopeAttached;
    }

    private static void OnScopeAttached(object sender, VisualTreeAttachmentEventArgs e) =>
        UIAppContext.Current?.ResourceManager?.NotifyResourcesChanged();
}
