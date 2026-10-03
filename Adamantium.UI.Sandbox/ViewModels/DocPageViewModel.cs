using Adamantium.MVVM;
using Adamantium.Navigation;
using Adamantium.UI.Sandbox.Localization;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>A document tab in the workspace window's region. Its <see cref="Number"/> (in the tab header) is stamped from
/// the navigation parameter, and it declines reuse so each OpenDoc yields a distinct tab.</summary>
[ViewModel]
public partial class DocPageViewModel : INavigationAware
{
    [Bindable] private int _number;

    /// <summary>What kind of page this is, by the key of its title among the workspace's phrases.</summary>
    public string Kind => nameof(WorkspaceStrings.Doc);

    public void OnNavigatedTo(NavigationContext context) => Number = context.Parameters.GetValue<int>("n");
    public void OnNavigatedFrom(NavigationContext context) { }
    public bool IsNavigationTarget(NavigationContext context) => false;
}
