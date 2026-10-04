using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Adamantium.Navigation;

/// <summary>Owns the app's regions. A view model creates an anonymous VM-owned region (<see cref="CreateRegion"/>) or
/// resolves a named one (<c>this[name]</c>/<see cref="GetOrCreateRegion"/>) that a control declares via
/// <c>RegionManager.RegionName</c>.</summary>
public interface IRegionManager
{
    IRegion this[string regionName] { get; }
    IRegion GetOrCreateRegion(string regionName);

    /// <summary>Create a region; a null name gets an auto-generated one (a VM owns it and binds it to a control).</summary>
    IRegion CreateRegion(string name = null);

    bool TryGetRegion(string regionName, out IRegion region);
    void RegisterRegion(IRegion region);
    IReadOnlyCollection<IRegion> Regions { get; }

    /// <summary>Navigates the region named <paramref name="regionName"/> in one call. A region no control has declared
    /// yet is created, and the control that declares it later shows what it navigated to.</summary>
    Task<NavigationResult> NavigateToAsync<TViewModel>(string regionName, NavigationParameters parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>Navigates the region named <paramref name="regionName"/> in one call - see
    /// <see cref="NavigateToAsync{TViewModel}(string, NavigationParameters, CancellationToken)"/>.</summary>
    Task<NavigationResult> NavigateToAsync(string regionName, Type viewModelType, NavigationParameters parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>The docking side of the region named <paramref name="regionName"/>: what its panes are and where. The
    /// same object before a docking control shows the region and after it is replaced.</summary>
    IDockingRegion Docking(string regionName);
}
