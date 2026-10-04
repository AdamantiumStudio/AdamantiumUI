namespace Adamantium.Navigation;

/// <summary>How a docking region shows this view model's pane. The title and the place follow the view model; the rest is
/// read once, when the pane is made. Without it, the type name is the pane's identity, so two view models of one type
/// share a pane.</summary>
public interface IDockablePane
{
    /// <summary>Identity in the layout. Two view models standing for different things must not share one.</summary>
    string PaneId { get; }

    /// <summary>The tab's label, followed while the pane is open.</summary>
    string PaneTitle { get; }

    /// <summary>Where the pane is; <see cref="DockZone.Center"/> is the document well. With a setter it is the pane's
    /// <c>Zone</c> for a view model: setting it moves the pane, and a drag sets it. Without one it only says where the
    /// pane opens.</summary>
    DockZone PaneZone { get; }

    /// <summary>Where the pane may be at all - the pane's <c>Allowed</c> for a view model. Distinct from
    /// <see cref="PaneZone"/>, which says where it IS: a pane opened floating may still be dockable, and
    /// <see cref="DockZone.Floating"/> alone is the float-only pane (Telerik's FloatingOnly) that can never come back
    /// into the layout.</summary>
    DockZone PaneAllowed { get; }

    /// <summary>A document, which closing ends, or a tool, which closing puts away.</summary>
    PaneKind PaneKind => PaneKind.Document;

    /// <summary>Smallest useful size along the docked axis.</summary>
    double PaneMinSize => 0;
}
