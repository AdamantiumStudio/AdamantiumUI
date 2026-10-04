using System.Threading;
using System.Threading.Tasks;

namespace Adamantium.Navigation;

/// <summary>Implemented by a view model that wants the navigation lifecycle: notification when it is navigated to/from,
/// and a say in whether an existing instance can serve a new navigation (view reuse). A navigation of the same region
/// started from these methods - a redirect - takes the place of the one in progress instead of waiting for it. The view
/// model of a window hears <see cref="OnNavigatedToAsync"/> too, before the window shows: the main window's, and one
/// opened by <see cref="INavigationService.OpenWindowAsync(System.Type, NavigationParameters, string, bool, CancellationToken)"/>.</summary>
public interface INavigationAware
{
    /// <summary>Awaited before the region shows this view model. Until it completes the previous one stays; if it throws
    /// or a newer navigation cancels <paramref name="cancellationToken"/>, the region does not change.</summary>
    Task OnNavigatedToAsync(NavigationContext context, CancellationToken cancellationToken = default);

    /// <summary>Awaited once the view model replacing this one is ready, before the region switches. Not called when this
    /// same instance serves the new navigation.</summary>
    Task OnNavigatedFromAsync(NavigationContext context, CancellationToken cancellationToken = default);

    /// <summary>Return true to REUSE this instance for <paramref name="context"/> instead of resolving a fresh one
    /// (e.g. the same page with different parameters).</summary>
    bool IsNavigationTarget(NavigationContext context);
}
