namespace Adamantium.Navigation;

/// <summary>One pane as a docking control reports it to its region.</summary>
public sealed class DockedPane
{
    public DockedPane(string id, object viewModel, PaneKind kind, PanePlacement placement)
    {
        Id = id;
        ViewModel = viewModel;
        Kind = kind;
        Placement = placement;
    }

    public string Id { get; }

    /// <summary>The view model the pane shows, or null for a pane without one.</summary>
    public object ViewModel { get; }

    public PaneKind Kind { get; }

    public PanePlacement Placement { get; }
}
