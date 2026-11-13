using System;

namespace Adamantium.UI.Core.Resources;

public interface IResourceManager
{
    // Raised (coalesced, once per layout pass via FlushResourceChanges) when the resource set changed - a theme swap, or
    // a dictionary loaded/unloaded. A live {ObservableResource} subscribes to re-resolve its key.
    event EventHandler ResourcesChanged;

    // Fire a pending ResourcesChanged if any AddSource/RemoveSources happened since the last flush. Called once at the
    // end of a layout pass, so a theme swap's remove-old + add-new collapse into ONE notification, AFTER the new theme's
    // dictionaries are loaded (no transient miss), and startup's many AddSource calls don't storm.
    void FlushResourceChanges();

    T FindResourceInScope<T>(string name, ResourceScope scope = ResourceScope.Local);
    
    object FindResourceInScope(string name, ResourceScope scope = ResourceScope.Local);
    
    object FindResource(string name);

    // Requester-aware: Local resources are visible only within the subtree of the element that declared them; walks up
    // from the requester. Context-less FindResource cannot see Local at all.
    object FindResource(IFundamentalUIComponent requester, string name);

    void AddSource(IAdamantiumComponent component, Type source, ResourceScope scope = ResourceScope.Local);

    // Register an inline (per-element) dictionary INSTANCE - ResourceContext.Resources authored on an element.
    void AddSource(IAdamantiumComponent component, ResourceDictionary instance, ResourceScope scope = ResourceScope.Local);

    /// <summary>Lets <paramref name="component"/> stop holding the dictionary of <paramref name="source"/> it linked, and
    /// what that links. A global one stays: global dictionaries outlive what linked them.</summary>
    void RemoveSource(IAdamantiumComponent component, Type source, ResourceScope scope = ResourceScope.Local);

    /// <summary>Takes back the inline dictionary <paramref name="instance"/> given to <paramref name="component"/>, and
    /// what it links.</summary>
    void RemoveSource(IAdamantiumComponent component, ResourceDictionary instance, ResourceScope scope = ResourceScope.Local);

    /// <summary>Adds to <paramref name="into"/> the styles of every dictionary <paramref name="owner"/> holds, in any
    /// scope - those of the files it links among them.</summary>
    void CollectStyles(IAdamantiumComponent owner, List<Style> into);

    /// <summary>A dictionary <paramref name="owner"/> holds as its own carries styles: it is a scope of styles for its
    /// logical subtree.</summary>
    bool HasStylesOf(IAdamantiumComponent owner);

    /// <summary>The styles of the scopes over <paramref name="element"/> that match it, each with the band its scope
    /// ranks it at: the application's outermost - for an element not part of a template - then those of its logical
    /// ancestors, the nearest highest. Null when none matches; <paramref name="underScope"/> tells whether an ancestor
    /// is a scope at all.</summary>
    Dictionary<Style, int> GetScopedStyles(IFundamentalUIComponent element, out bool underScope);

    // A resource VALUE changed in place (an inline dictionary was mutated): re-resolve live {ObservableResource}s.
    void NotifyResourcesChanged();

    // Runtime resource override: replace the VALUE of an existing keyed resource in the given scope (e.g. cycle a theme
    // palette entry live). The dictionary that currently resolves the key gets the new value and live
    // {ObservableResource}s re-resolve (coalesced, this pass); a {ResourceReference} keeps its resolved-once value.
    // Returns false when no dictionary in that scope declares the key (nothing changed, no notification raised).
    bool SetResourceInScope(string name, object value, ResourceScope scope = ResourceScope.Local);

    void RemoveSources(IAdamantiumComponent component);

    /// <summary>Stops holding <paramref name="component"/> as an owner of the global dictionaries it linked, keeping the
    /// dictionaries: global resources outlive what linked them. For an element that has been destroyed.</summary>
    void ForgetGlobalSourceOwner(IAdamantiumComponent component);
    
    Object this[string name] { get; }
}