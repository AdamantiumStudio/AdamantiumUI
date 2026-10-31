namespace Adamantium.UI.Core.Resources;

public class ResourceProvider
{
    private readonly List<ResourceInfo> _orderedDictionaries = new();
    private readonly Dictionary<string, object> _cache = new();
    private readonly Dictionary<Type, List<string>> _cacheByDictionary = new();
    private readonly Dictionary<Type, ResourceInfo> _loadedDictionaries = new();
    // owner -> the dictionaries it owns, in add order. Lets FindResourceForOwner / RemoveOwner touch only THIS owner's
    // handful of dictionaries instead of scanning every dictionary in the scope (the tree-walk lookup asks this per
    // ancestor, so a global scan was O(ancestors x all-dictionaries) per resolve).
    private readonly Dictionary<IAdamantiumComponent, List<ResourceInfo>> _byOwner = new();
    private int _styledDictionaries;

    /// <summary>Whether a dictionary some element owns here carries unkeyed styles - none usually, and then no element
    /// looks for them.</summary>
    public bool HasStyles => _styledDictionaries > 0;

    /// <summary>Registers the dictionary of <paramref name="sourceType"/> under <paramref name="owner"/>, creating it the
    /// first time - by <paramref name="create"/> when given. <paramref name="linkIncludes"/> registers what it links
    /// before it, so its own keys win over theirs.</summary>
    public void AddSource(IAdamantiumComponent owner, Type sourceType, Action<ResourceDictionary> linkIncludes = null,
        Func<Type, ResourceDictionary> create = null)
    {
        var isNew = !_loadedDictionaries.TryGetValue(sourceType, out var info);
        if (isNew)
        {
            info = new ResourceInfo(sourceType, create?.Invoke(sourceType));
            _loadedDictionaries[sourceType] = info;
        }

        if (Link(info, owner))
        {
            linkIncludes?.Invoke(info.Resource);
        }

        if (isNew)
        {
            _orderedDictionaries.Add(info);
            InvalidateCache();
        }
    }

    /// <summary>Takes back one link of <paramref name="owner"/> to the dictionary of <paramref name="sourceType"/>; the
    /// dictionary goes once nothing links it. Returns it when that was the owner's last link to it, else null.</summary>
    public ResourceDictionary RemoveSource(IAdamantiumComponent owner, Type sourceType)
    {
        if (!_loadedDictionaries.TryGetValue(sourceType, out var info) || !Unlink(info, owner, all: false))
        {
            return null;
        }

        if (info.Links.Count == 0)
        {
            _loadedDictionaries.Remove(sourceType);
            _orderedDictionaries.Remove(info);
            InvalidateCache();
        }

        return info.Resource;
    }

    // An INLINE (per-element) dictionary: a unique instance authored right on the element (ResourceContext.Resources),
    // not a shared dictionary TYPE. No type-dedup (each instance is its own) - just register it under this owner, so it
    // is tree-scoped and freed with the owner exactly like a linked type is.
    public void AddSource(IAdamantiumComponent owner, ResourceDictionary instance)
    {
        var info = new ResourceInfo(instance);
        _orderedDictionaries.Add(info);
        Link(info, owner);
        InvalidateCache();
    }

    /// <summary>Stops counting <paramref name="owner"/> among the owners of its dictionaries but keeps the dictionaries,
    /// for a scope whose resources outlive what linked them.</summary>
    public void ForgetOwner(IAdamantiumComponent owner)
    {
        if (!_byOwner.Remove(owner, out var owned))
        {
            return;
        }

        foreach (var info in owned)
        {
            Unlink(info, owner, all: true);
        }
    }

    public void RemoveOwner(IAdamantiumComponent owner)
    {
        if (!_byOwner.Remove(owner, out var owned)) return;

        List<ResourceInfo> abandonedInfos = null;
        foreach (var info in owned)
        {
            if (Unlink(info, owner, all: true) && info.Links.Count == 0)
            {
                (abandonedInfos ??= new List<ResourceInfo>()).Add(info);
            }
        }

        if (abandonedInfos != null)
        {
            foreach (var info in abandonedInfos)
            {
                if (info.SourceType != null) _loadedDictionaries.Remove(info.SourceType);   // inline instances aren't in the type map
                _orderedDictionaries.Remove(info);
            }

            InvalidateCache();
        }
    }

    public object FindResource(string key)
    {
        if (_cache.TryGetValue(key, out var resource))
        {
            return resource;
        }

        for (int i = _orderedDictionaries.Count - 1; i >= 0; i--)
        {
            var info = _orderedDictionaries[i];
            if (info.Resource.TryGetValue(key, out resource))
            {
                _cache[key] = resource;
                return resource;
            }
        }
        return null;
    }

    /// <summary>Replaces the VALUE of an existing keyed resource in whichever loaded dictionary currently RESOLVES that
    /// key (the same last-declared-wins order as <see cref="FindResource"/>) - the runtime resource override (e.g.
    /// cycling a palette entry live). Only that key's cache entry is evicted. Returns false when no loaded dictionary
    /// declares the key (nothing to override); the caller then must not raise a change notification.</summary>
    public bool SetResource(string key, object value)
    {
        for (int i = _orderedDictionaries.Count - 1; i >= 0; i--)
        {
            var info = _orderedDictionaries[i];
            if (info.Resource.TryGetValue(key, out _))
            {
                info.Resource[key] = value;
                _cache.Remove(key);
                return true;
            }
        }
        return false;
    }

    // A key from dictionaries owned by EXACTLY this element. The caller (ResourceManager) walks the tree and asks each
    // ancestor in turn, so a Local resource is visible only inside the owner's subtree - never from an unrelated element.
    // Not cached: the same key can resolve to different values under different owners, so the flat _cache would leak
    // across scopes. Iterate only this owner's own dictionaries (via _byOwner), newest-first for last-declared-wins.
    public object FindResourceForOwner(IAdamantiumComponent owner, string key)
    {
        if (!_byOwner.TryGetValue(owner, out var owned)) return null;
        for (int i = owned.Count - 1; i >= 0; i--)
        {
            if (owned[i].Resource.TryGetValue(key, out var resource))
            {
                return resource;
            }
        }
        return null;
    }

    /// <summary>Adds to <paramref name="into"/> - made on the first match - the unkeyed styles of the dictionaries
    /// <paramref name="owner"/> owns that match <paramref name="component"/>, in the order they were added; a style
    /// already there moves to the end. <paramref name="ownsStyles"/> turns true when any of them carries styles.</summary>
    public void CollectStylesForOwner(IAdamantiumComponent owner, IFundamentalUIComponent component,
        ref List<Style> into, ref bool ownsStyles)
    {
        if (_styledDictionaries == 0 || !_byOwner.TryGetValue(owner, out var owned))
        {
            return;
        }

        foreach (var info in owned)
        {
            if (!info.CountsStyles)
            {
                continue;
            }

            ownsStyles = true;
            var styles = info.Resource.Styles;
            for (var i = 0; i < styles.Count; i++)
            {
                var style = styles[i];
                if (!style.Selector.Match(component))
                {
                    continue;
                }

                style.MarkScoped();
                into ??= [];
                into.Remove(style);
                into.Add(style);
            }
        }
    }

    /// <summary>Takes back <paramref name="instance"/> from <paramref name="owner"/>; dropped once no owner has it.
    /// Returns whether the owner had it here.</summary>
    public bool RemoveInstance(IAdamantiumComponent owner, ResourceDictionary instance)
    {
        if (!_byOwner.TryGetValue(owner, out var owned))
        {
            return false;
        }

        ResourceInfo removed = null;
        foreach (var info in owned)
        {
            if (ReferenceEquals(info.Resource, instance))
            {
                removed = info;
                break;
            }
        }

        if (removed == null)
        {
            return false;
        }

        Unlink(removed, owner, all: true);
        if (removed.Links.Count == 0)
        {
            _orderedDictionaries.Remove(removed);
            InvalidateCache();
        }

        return true;
    }

    private bool Link(ResourceInfo info, IAdamantiumComponent owner)
    {
        info.Links.TryGetValue(owner, out var links);
        info.Links[owner] = links + 1;
        if (links > 0)
        {
            return false;
        }

        if (!_byOwner.TryGetValue(owner, out var owned))
        {
            owned = new List<ResourceInfo>();
            _byOwner[owner] = owned;
        }

        owned.Add(info);
        if (!info.CountsStyles && info.Resource.Styles.Count > 0)
        {
            info.CountsStyles = true;
            _styledDictionaries++;
        }

        return true;
    }

    private bool Unlink(ResourceInfo info, IAdamantiumComponent owner, bool all)
    {
        if (!info.Links.TryGetValue(owner, out var links))
        {
            return false;
        }

        if (!all && links > 1)
        {
            info.Links[owner] = links - 1;
            return false;
        }

        info.Links.Remove(owner);
        if (_byOwner.TryGetValue(owner, out var owned) && owned.Remove(info) && owned.Count == 0)
        {
            _byOwner.Remove(owner);
        }

        if (info.Links.Count == 0 && info.CountsStyles)
        {
            info.CountsStyles = false;
            _styledDictionaries--;
        }

        return true;
    }

    private void AddCacheFor(Type type, string key)
    {
        if (!_cacheByDictionary.TryGetValue(type, out var keys))
        {
            keys = new List<string>();
            _cacheByDictionary.Add(type, keys);
        }
        keys.Add(key);
    }

    public void ClearCacheFor(Type type)
    {
        if (!_cacheByDictionary.TryGetValue(type, out var keys))
            return;

        foreach (var key in keys)
        {
            _cache.Remove(key);
        }
    }

    public void InvalidateCache()
    {
        _cache.Clear();
    }

    private class ResourceInfo
    {
        public ResourceDictionary Resource { get; }

        // The dictionary TYPE when this is a shared/deduped linked dictionary; null for an inline instance (which is
        // unique and not tracked in _loadedDictionaries).
        public Type SourceType { get; }

        public Dictionary<IAdamantiumComponent, int> Links { get; } = new();

        public bool CountsStyles { get; set; }

        public ResourceInfo(Type dictionaryType, ResourceDictionary made)
        {
            SourceType = dictionaryType;
            Resource = made ?? (ResourceDictionary)Activator.CreateInstance(dictionaryType);
            Resource?.Initialize();
        }

        public ResourceInfo(ResourceDictionary instance)
        {
            Resource = instance;
            instance.Initialize();
        }
    }
}
