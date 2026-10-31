namespace Adamantium.UI.Core.Resources;

public class ResourceManager : IResourceManager
{
    private ResourceProvider _localResources { get; }
    private ResourceProvider _globalResources { get; }
    private ResourceProvider _themeResources { get; }

    // Coalesced change signal for live {ObservableResource}s. A mutation only sets the flag; the actual event fires once
    // per layout pass (FlushResourceChanges), so a theme swap - which removes the old sources then adds the new ones
    // during the pass's re-theme - notifies ONCE, after everything settled, and startup's bulk AddSource never storms.
    public event EventHandler ResourcesChanged;
    private bool _resourcesDirty;

    public void FlushResourceChanges()
    {
        if (!_resourcesDirty) return;
        _resourcesDirty = false;
        ResourcesChanged?.Invoke(this, EventArgs.Empty);
    }

    public ResourceManager()
    {
        _localResources = new ResourceProvider();
        _globalResources = new ResourceProvider();
        _themeResources = new ResourceProvider();
    }

    private static readonly Lazy<Dictionary<string, Type>> _uriToTypeMap = new Lazy<Dictionary<string, Type>>(() =>
    {
        var map = new Dictionary<string, Type>();
        Initialize(map);
        return map;
    });

    private static void Initialize(Dictionary<string, Type> map)
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        foreach (var assembly in assemblies)
        {
            if (!assembly.IsDefined(typeof(ContainsResourcesAttribute), false)) 
                continue;
            try
            {
                var registrarType = assembly.GetTypes()
                    .FirstOrDefault(t => t.IsDefined(typeof(ResourceMapRegistrarAttribute), false));

                if (registrarType != null)
                {
                    var registerMethod = registrarType.GetMethod("Register",
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

                    if (registerMethod != null)
                    {
                        registerMethod.Invoke(null, [map]);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Resource Scanner] Failed to process assembly {assembly.FullName}: {ex.Message}");
            }
        }
    }
    
    // Context-less lookup: Local scope is DELIBERATELY invisible here. A Local resource is only reachable through the
    // requester-aware overload below (which tree-scopes it), so a lookup with no requesting element - a global/eager
    // resolve - can never reach into a subtree's Local dictionary. Only Theme and Global are reachable this way.
    public object FindResource(string name)
    {
        if (_uriToTypeMap.Value.TryGetValue(name, out var type))
        {
            return type;
        }

        var resource = FindResourceInScope(name, ResourceScope.Theme);
        if (resource != null)
            return resource;

        // The APPLICATION theme's palette - the right answer when there is no requester, since with nobody to scope
        // from the application's theme is the only theme in force. Without this, a theme that keeps its palette in
        // variants rather than in a resource dictionary would answer every requester-aware lookup and none of the
        // eager ones, which is a difference no theme author could see coming.
        if (UIAppContext.Current?.ThemeManager?.CurrentTheme is Theme current
            && current.PaletteValue(name) is { } fromPalette)
        {
            return fromPalette;
        }

        return FindResourceInScope(name, ResourceScope.Global);
    }

    // Requester-aware lookup: Local resources are TREE-SCOPED. Walk up from the requesting element (logical parent, or
    // the visual parent where there's no logical one - e.g. templated content), and at each ancestor look only at
    // dictionaries that ancestor OWNS. Nearest owner wins; then fall back to Theme, then Global. This is what makes a
    // Local resource reachable from the subtree that declared it - and nowhere else.
    public object FindResource(IFundamentalUIComponent requester, string name)
    {
        if (_uriToTypeMap.Value.TryGetValue(name, out var type))
        {
            return type;
        }

        for (var node = requester; node != null; node = LogicalOrVisualParent(node))
        {
            var local = _localResources.FindResourceForOwner(node, name);
            if (local != null)
                return Materialize(local);
        }

        // The palette of the theme in force at this element, after Local dictionaries and before the global scope;
        // per requester, since subtrees may show different variants.
        if (ThemeContext.For(requester) is Theme scoped && scoped.PaletteValue(name) is { } fromPalette)
            return fromPalette;

        var themeResource = FindResourceInScope(name, ResourceScope.Theme);
        if (themeResource != null)
            return themeResource;

        return FindResourceInScope(name, ResourceScope.Global);
    }

    // Logical parent first (bridging a template boundary via TemplatedParent, so template content still sees resources
    // declared on its host control), then the visual parent as a last resort for a node with neither.
    private static IFundamentalUIComponent LogicalOrVisualParent(IFundamentalUIComponent node)
        => node.GetLogicalParentOrBridge() ?? (node as IUIComponent)?.VisualParent;

    public T FindResourceInScope<T>(string name, ResourceScope scope = ResourceScope.Local)
    {
        var resource = FindResourceInScope(name, scope);
        return (T)resource;
    }

    public object FindResourceInScope(string name, ResourceScope scope = ResourceScope.Local)
    {
        object value = null;
        switch (scope)
        {
            case ResourceScope.Local:
                value = _localResources.FindResource(name);
                break;
            case ResourceScope.Global:
                value = _globalResources.FindResource(name);
                break;
            case ResourceScope.Theme:
                value = _themeResources.FindResource(name);
                break;
        }

        return Materialize(value);
    }

    // An entry declared x:Shared="False" is STORED as a factory, so every ask gets its own object - what a ContextMenu, a
    // Popup or a Transform needs, each belonging to the element it sits on. Unwrapped HERE, the one place a stored
    // resource leaves the dictionary, so no consumer has to know a factory was ever involved.
    private static object Materialize(object stored) => stored is PerTargetValue perTarget ? perTarget.Create() : stored;

    public void AddSource(IAdamantiumComponent owner, Type source, ResourceScope scope = ResourceScope.Local)
    {
        var create = typeof(StyleSet).IsAssignableFrom(source) ? StylesOf : (Func<Type, ResourceDictionary>)null;
        ProviderOf(scope).AddSource(owner, source, dictionary => AddIncludes(owner, dictionary, scope), create);
        _resourcesDirty = true;
    }

    // Register an INLINE (per-element) dictionary instance - the ResourceContext.Resources authored right on an element -
    // rather than a shared dictionary type. Same scoping/lifecycle as a linked type.
    public void AddSource(IAdamantiumComponent owner, ResourceDictionary instance, ResourceScope scope = ResourceScope.Local)
    {
        AddIncludes(owner, instance, scope);
        ProviderOf(scope).AddSource(owner, instance);
        _resourcesDirty = true;
    }

    public void RemoveSource(IAdamantiumComponent owner, Type source, ResourceScope scope = ResourceScope.Local)
    {
        if (ProviderOf(scope).RemoveSource(owner, source) is { } removed)
        {
            RemoveIncludes(owner, removed, scope);
        }

        _resourcesDirty = true;
    }

    public void RemoveSource(IAdamantiumComponent owner, ResourceDictionary instance,
        ResourceScope scope = ResourceScope.Local)
    {
        var heldIn = _localResources.RemoveInstance(owner, instance) ? ResourceScope.Local
            : _globalResources.RemoveInstance(owner, instance) ? ResourceScope.Global
            : _themeResources.RemoveInstance(owner, instance) ? ResourceScope.Theme
            : scope;
        RemoveIncludes(owner, instance, heldIn);
        _resourcesDirty = true;
    }

    public bool HasScopedStyles => _localResources.HasStyles || _globalResources.HasStyles || _themeResources.HasStyles;

    public List<Style> CollectScopedStyles(IFundamentalUIComponent component, out bool inStyledScope)
    {
        inStyledScope = false;
        if (component == null || !HasScopedStyles)
        {
            return null;
        }

        List<Style> into = null;
        CollectScopedStyles(component, component, ref into, ref inStyledScope);
        return into;
    }

    private void CollectScopedStyles(IFundamentalUIComponent owner, IFundamentalUIComponent component,
        ref List<Style> into, ref bool inStyledScope)
    {
        if (owner == null)
        {
            return;
        }

        CollectScopedStyles(ScopeParent(owner), component, ref into, ref inStyledScope);
        _themeResources.CollectStylesForOwner(owner, component, ref into, ref inStyledScope);
        _globalResources.CollectStylesForOwner(owner, component, ref into, ref inStyledScope);
        _localResources.CollectStylesForOwner(owner, component, ref into, ref inStyledScope);
    }

    private static IFundamentalUIComponent ScopeParent(IFundamentalUIComponent node)
    {
        if (node is FundamentalUIComponent { IsControlTemplatePart: true } part)
        {
            return part.LogicalParent is FundamentalUIComponent inside &&
                   inside.ControlTemplatePartOf == part.ControlTemplatePartOf
                ? inside
                : null;
        }

        var parent = node.LogicalParent ?? node.TemplatedParent as IFundamentalUIComponent ??
            (node as IUIComponent)?.VisualParent ?? (node as FundamentalUIComponent)?.DetachedScopeParent;
        if (parent is not FundamentalUIComponent { IsControlTemplatePart: true } holder)
        {
            return parent;
        }

        return holder.ContentGiver;
    }

    private static ResourceDictionary StylesOf(Type styleSetType)
    {
        var styleSet = (StyleSet)Activator.CreateInstance(styleSetType);
        styleSet.Initialize(UIAppContext.Current?.ThemeManager?.CurrentTheme);
        var dictionary = new ResourceDictionary();
        foreach (var style in styleSet.Styles)
        {
            style.Theme = null;
            dictionary.Styles.Add(style);
        }

        return dictionary;
    }

    private void AddIncludes(IAdamantiumComponent owner, ResourceDictionary dictionary, ResourceScope scope)
    {
        foreach (var include in dictionary.Includes)
        {
            if (include?.Source != null)
            {
                AddSource(owner, include.Source, include.Scope == ResourceScope.Local ? scope : include.Scope);
            }
        }
    }

    private void RemoveIncludes(IAdamantiumComponent owner, ResourceDictionary dictionary, ResourceScope scope)
    {
        foreach (var include in dictionary.Includes)
        {
            if (include?.Source != null)
            {
                RemoveSource(owner, include.Source, include.Scope == ResourceScope.Local ? scope : include.Scope);
            }
        }
    }

    private ResourceProvider ProviderOf(ResourceScope scope) => scope switch
    {
        ResourceScope.Global => _globalResources,
        ResourceScope.Theme => _themeResources,
        _ => _localResources,
    };

    public void NotifyResourcesChanged()
    {
        if (LoopSignal.IsClaimed && !LoopSignal.IsLoopThread)
        {
            LoopSignal.Post(NotifyResourcesChanged);
            return;
        }

        // A resource VALUE changed IN PLACE (an inline dictionary entry was reassigned) rather than a whole dictionary
        // being added/removed: drop the cached lookups so the next resolve re-reads, and mark dirty so the coalesced
        // ResourcesChanged fires this pass - live {ObservableResource}s then pick up the new value.
        _localResources.InvalidateCache();
        _themeResources.InvalidateCache();
        _globalResources.InvalidateCache();
        _resourcesDirty = true;
    }

    public bool SetResourceInScope(string name, object value, ResourceScope scope = ResourceScope.Local)
    {
        ResourceProvider provider;
        switch (scope)
        {
            case ResourceScope.Local:
                provider = _localResources;
                break;
            case ResourceScope.Global:
                provider = _globalResources;
                break;
            case ResourceScope.Theme:
                provider = _themeResources;
                break;
            default:
                return false;
        }

        if (!provider.SetResource(name, value)) return false;
        // The provider already evicted just the overridden key from ITS cache; mark dirty so the coalesced
        // ResourcesChanged fires this pass and live {ObservableResource}s re-resolve.
        _resourcesDirty = true;
        return true;
    }

    public void RemoveSources(IAdamantiumComponent component)
    {
        if (component == null) return;

        _localResources.RemoveOwner(component);
        _themeResources.RemoveOwner(component);
        _globalResources.RemoveOwner(component);
        _resourcesDirty = true;
    }

    public void ForgetGlobalSourceOwner(IAdamantiumComponent component)
    {
        if (component == null)
        {
            return;
        }

        _globalResources.ForgetOwner(component);
    }

    public object this[string name] => FindResource(name);
}