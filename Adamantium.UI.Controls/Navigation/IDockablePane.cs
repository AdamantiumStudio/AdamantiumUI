using Adamantium.UI.Controls.Docking;

namespace Adamantium.UI.Controls.Navigation;

/// <summary>How a docking region opens a pane for this view model; read only at creation. Without it, the type name is the
/// pane's identity, so two view models of one type share a pane.</summary>
public interface IDockablePane
{
    /// <summary>Identity in the layout. Two view models standing for different things must not share one.</summary>
    string PaneId { get; }

    /// <summary>The tab's label.</summary>
    string PaneTitle { get; }

    /// <summary>Where a new pane goes; <see cref="DockZone.Center"/> is the document well.</summary>
    DockZone PaneZone { get; }

    /// <summary>Where the pane may be at all - <see cref="Pane.Allowed"/> for a view model. Distinct from
    /// <see cref="PaneZone"/>, which only says where it OPENS: a pane opened floating may still be dockable, and
    /// <see cref="DockZone.Floating"/> alone is the float-only pane (Telerik's FloatingOnly) that can never come back
    /// into the layout.</summary>
    DockZone PaneAllowed { get; }
}
