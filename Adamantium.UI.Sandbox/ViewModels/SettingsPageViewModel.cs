using Adamantium.MVVM;
using Adamantium.Navigation;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>Settings page: shows the navigation lifecycle (INavigationAware) by stamping how it was reached.</summary>
[ViewModel]
public partial class SettingsPageViewModel : INavigationAware
{
    [Bindable] private NavigationMode? _arrivedVia;

    public void OnNavigatedTo(NavigationContext context) => ArrivedVia = context.Mode;
    public void OnNavigatedFrom(NavigationContext context) { }
    public bool IsNavigationTarget(NavigationContext context) => true;
}
