using System;

namespace Adamantium.Navigation;

public class DockingPaneEventArgs : EventArgs
{
    public DockingPaneEventArgs(object viewModel, PanePlacement placement)
    {
        ViewModel = viewModel;
        Placement = placement;
    }

    public object ViewModel { get; }

    /// <summary>Where the pane is now; for a closed one, where it was.</summary>
    public PanePlacement Placement { get; }
}
