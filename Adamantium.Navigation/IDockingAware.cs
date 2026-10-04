namespace Adamantium.Navigation;

/// <summary>What a view model in a docking region hears about its own pane; implement only what matters. Called in the
/// order of a change: the pane left behind first (deactivated, hidden), then the move, then the pane arrived at (shown,
/// activated).</summary>
public interface IDockingAware
{
    /// <summary>Its pane became the one being worked in.</summary>
    void OnActivated() { }

    void OnDeactivated() { }

    /// <summary>Its pane came on screen: the front tab of a panel that is not folded away.</summary>
    void OnShown() { }

    /// <summary>Its pane went off screen - behind another tab, folded away, closed. What a scene view or a profiler stops
    /// working for.</summary>
    void OnHidden() { }

    /// <summary>Its pane went to another zone, or folded away, or came back from its strip.</summary>
    void OnPlacementChanged(PanePlacement placement) { }
}
