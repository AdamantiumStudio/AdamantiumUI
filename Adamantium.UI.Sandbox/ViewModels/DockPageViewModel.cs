using System.Threading;
using System.Threading.Tasks;
using Adamantium.MVVM;
using Adamantium.Navigation;
using Adamantium.UI.Controls.Docking;
using Adamantium.UI.Controls.Navigation;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>A pane opened into the docking area BY NAVIGATION rather than by the markup. Its name is its identity: asking
/// for a name that is already open reuses this instance (<see cref="IsNavigationTarget"/>), which is what makes the
/// region activate the existing tab instead of opening a second one just like it.</summary>
[ViewModel]
public partial class DockPageViewModel : INavigationAware, IDockablePane, IRestorablePane, IDocument
{
    public const string PageKey = "page";
    public const string ZoneKey = "zone";

    [Bindable] private string _title = "Page";

    /// <summary>Where the pane was first opened - or that it came back with a saved layout instead.</summary>
    [Bindable] private DockZone _openedIn;
    [Bindable] private bool _restored;

    /// <summary>Where the pane is: a drag writes it, and setting it moves the pane.</summary>
    [Bindable] private DockZone _paneZone = DockZone.Center;

    /// <summary>Work not saved yet: the tab shows it, and closing the page asks whether to save.</summary>
    [Bindable] private bool _isDirty;

    public string PaneId => Title;
    public string PaneTitle => Title;

    public Task<bool> SaveAsync(CancellationToken cancellationToken = default)
    {
        IsDirty = false;
        return Task.FromResult(true);
    }

    /// <summary>Opened floating means float-ONLY here, so the demo shows both halves of the idea: where a pane opens
    /// (<see cref="OpenedIn"/>) and where it may ever be (this). Docked ones stay dockable anywhere.</summary>
    public DockZone PaneAllowed => OpenedIn == DockZone.Floating ? DockZone.Floating : DockZone.All;

    public Task OnNavigatedToAsync(NavigationContext context, CancellationToken cancellationToken = default)
    {
        Title = context.Parameters.GetValue(PageKey, Title);

        // Only where it is first opened: the zone the pane already lives in belongs to the user by then.
        if (OpenedIn == DockZone.None && !Restored)
        {
            PaneZone = context.Parameters.GetValue(ZoneKey, DockZone.Center);
            OpenedIn = PaneZone;
        }

        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync(NavigationContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>Back from a saved layout: the name IS the identity here, so the pane's id is the whole of what this
    /// page has to remember. Where it lands is the layout's business, not its own.</summary>
    public void RestoreFrom(string paneId)
    {
        Title = paneId;
        Restored = true;
    }

    public bool IsNavigationTarget(NavigationContext context)
    {
        return Title == context.Parameters.GetValue<string>(PageKey);
    }
}
