using System.Threading;
using System.Threading.Tasks;
using Adamantium.MVVM;
using Adamantium.Navigation;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>Settings page: shows the navigation lifecycle (INavigationAware) by stamping how it was reached.</summary>
[ViewModel]
public partial class SettingsPageViewModel : INavigationAware
{
    [Bindable] private NavigationMode? _arrivedVia;

    public Task OnNavigatedToAsync(NavigationContext context, CancellationToken cancellationToken = default)
    {
        ArrivedVia = context.Mode;
        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync(NavigationContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public bool IsNavigationTarget(NavigationContext context) => true;
}
