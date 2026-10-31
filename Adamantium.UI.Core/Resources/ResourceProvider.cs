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

    /// <summary>Registers the dictionary of <paramref name="sourceType"/> under <paramref name="owner"/>, creating it the
    /// first time. <paramref name="linkIncludes"/> registers what it links before it, so its own keys win over theirs.</summary>
    public ResourceDictionary AddSource(IAdamantiumComponent owner, Type sourceType, Action<ResourceDictionary> linkIncludes = null)
    {
        var isNew = !_loadedDictionaries.TryGetValue(sourceType, out var info);
        if (isNew)
        {
            info = new ResourceInfo(sourceType);
            _loadedDictionaries[sourceType] = info;
        }

        if (info.Owners.Add(owner))
        {
            linkIncludes?.Invoke(info.Resource);
            if (!_byOwner.TryGetValue(owner, out var owned))
            {
                owned = new List<ResourceInfo>();
                _byOwner[owner] = owned;
            }
            owned.Add(info);
        }

        if (isNew)
        {
            Load(info);
        }

        return info.Resource;
    }

    // An INLINE (per-element) dictionary: a unique instance authored right on the element (ResourceContext.Resources),
    // not a shared dictionary TYPE. No type-dedup (each instance is its own) - just register it under this owner, so it
    // is tree-scoped and freed with the owner exactly like a linked type is.
    public void AddSource(IAdamantiumComponent owner, ResourceDictionary instance)
    {
        var info = new ResourceInfo(instance);
        info.Owners.Add(owner);
        if (!_byOwner.TryGetValue(owner, out var owned))
        {
            owned = new List<ResourceInfo>();
            _byOwner[owner] = owned;
        }
        owned.Add(info);
        Load(info);
    }

    /// <summary>Lets <paramref name="owner"/> stop holding the dictionary of <paramref name="sourceType"/> it linked, and
    /// returns it; the dictionary goes once nothing holds it. Null when the owner did not hold it.</summary>
    public ResourceDictionary RemoveSource(IAdamantiumComponent owner, Type sourceType)
    {
        if (!_loadedDictionaries.TryGetValue(sourceType, out var info) || !info.Owners.Contains(owner))
        {
            return null;
        }

        Release(owner, info);
        return info.Resource;
    }

    /// <summary>Takes back the inline dictionary <paramref name="instance"/> that <paramref name="owner"/> was given.</summary>
    public void RemoveSource(IAdamantiumComponent owner, ResourceDictionary instance)
    {
        if (!_byOwner.TryGetValue(owner, out var owned))
        {
            return;
        }

        foreach (var info in owned)
        {
            if (info.SourceType == null && ReferenceEquals(info.Resource, instance))
            {
                Release(owner, info);
                return;
            }
        }
    }

    /// <summary>Some dictionary of this scope carries styles.</summary>
    public bool HasStyles => _styledDictionaries > 0;

    /// <summary>A dictionary <paramref name="owner"/> holds carries styles.</summary>
    public bool HasStylesOf(IAdamantiumComponent owner)
    {
        if (_styledDictionaries == 0 || !_byOwner.TryGetValue(owner, out var owned))
        {
            return false;
        }

        foreach (var info in owned)
        {
            if (info.Resource.Styles.Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Adds to <paramref name="into"/> the styles of the dictionaries <paramref name="owner"/> holds, oldest
    /// first.</summary>
    public void CollectStyles(IAdamantiumComponent owner, List<Style> into)
    {
        if (!_byOwner.TryGetValue(owner, out var owned))
        {
            return;
        }

        foreach (var info in owned)
        {
            into.AddRange(info.Resource.Styles);
        }
    }

    /// <summary>Adds to <paramref name="into"/> the styles of every dictionary of this scope, oldest first.</summary>
    public void CollectStyles(List<Style> into)
    {
        foreach (var info in _orderedDictionaries)
        {
            into.AddRange(info.Resource.Styles);
        }
    }

    private void Load(ResourceInfo info)
    {
        _orderedDictionaries.Add(info);
        InvalidateCache();
    }

    private void Unload(ResourceInfo info)
    {
        if (info.SourceType != null)
        {
            _loadedDictionaries.Remove(info.SourceType);
        }

        _orderedDictionaries.Remove(info);
    }

    private void Release(IAdamantiumComponent owner, ResourceInfo info)
    {
        if (!info.Owners.Remove(owner))
        {
            return;
        }

        if (_byOwner.TryGetValue(owner, out var owned) && owned.Remove(info) && owned.Count == 0)
        {
            _byOwner.Remove(owner);
        }

        if (info.Owners.Count == 0)
        {
            Unload(info);
            InvalidateCache();
        }
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
            info.Owners.Remove(owner);
        }
    }

    public void RemoveOwner(IAdamantiumComponent owner)
    {
        if (!_byOwner.TryGetValue(owner, out var owned)) return;
        _byOwner.Remove(owner);

        List<ResourceInfo> abandonedInfos = null;
        foreach (var info in owned)
        {
            if (info.Owners.Remove(owner) && info.Owners.Count == 0)
            {
                (abandonedInfos ??= new List<ResourceInfo>()).Add(info);
            }
        }

        if (abandonedInfos != null)
        {
            foreach (var info in abandonedInfos)
            {
                Unload(info);
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

    /// <summary>Drops the cached lookups and counts again the dictionaries carrying styles: called whenever the loaded
    /// dictionaries or their content change, a dictionary refilled in place by a reload among them.</summary>
    public void InvalidateCache()
    {
        _cache.Clear();
        _styledDictionaries = 0;
        foreach (var info in _orderedDictionaries)
        {
            if (info.Resource.Styles.Count > 0)
            {
                _styledDictionaries++;
            }
        }
    }
    
    private class ResourceInfo
    {
        public ResourceDictionary Resource { get; }

        // The dictionary TYPE when this is a shared/deduped linked dictionary; null for an inline instance (which is
        // unique and not tracked in _loadedDictionaries).
        public Type SourceType { get; }

        public HashSet<IAdamantiumComponent> Owners { get; } = new();

        public ResourceInfo(Type dictionaryType)
        {
            SourceType = dictionaryType;
            Resource = (ResourceDictionary)Activator.CreateInstance(dictionaryType);
            Resource?.Initialize();
        }

        public ResourceInfo(ResourceDictionary instance)
        {
            Resource = instance;
            instance.Initialize();
        }
    }
}