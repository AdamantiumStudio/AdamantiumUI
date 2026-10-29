namespace Adamantium.Navigation;

/// <summary>How a docking region shows this view model's pane. Only <see cref="PaneTitle"/> has to be given; the rest
/// has a default. The title, the icon and the place follow the view model, the rest is read once, when the pane is made.
/// Without this interface, the type name is the pane's identity, so two view models of one type share a pane.</summary>
public interface IDockablePane
{
    /// <summary>Identity in the layout, the type's name by default. Two view models standing for different things must
    /// not share one.</summary>
    string PaneId => GetType().Name;

    /// <summary>The tab's label, followed while the pane is open and read again when the application's language
    /// changes - a string of a language table needs nothing more.</summary>
    string PaneTitle { get; }

    /// <summary>Where the pane is; <see cref="DockZone.Center"/>, the document well, by default. Declared with a setter
    /// it is the pane's <c>Zone</c> for a view model: setting it moves the pane, and a drag sets it. Without one it only
    /// says where the pane opens.</summary>
    DockZone PaneZone => DockZone.Center;

    /// <summary>Where the pane may be at all - the pane's <c>Allowed</c> for a view model, anywhere by default. Distinct
    /// from <see cref="PaneZone"/>, which says where it IS: a pane opened floating may still be dockable, and
    /// <see cref="DockZone.Floating"/> alone is the float-only pane (Telerik's FloatingOnly) that can never come back
    /// into the layout.</summary>
    DockZone PaneAllowed => DockZone.All;

    /// <summary>A document, which closing ends, or a tool, which closing puts away.</summary>
    PaneKind PaneKind => PaneKind.Document;

    /// <summary>Smallest useful size along the docked axis.</summary>
    double PaneMinSize => 0;

    /// <summary>The key of the tab's icon among the resources - a picture, or path data drawn in the tab's text color -
    /// shown in front of the title; none by default. Followed as the title is, and the resource follows a theme swap.
    /// </summary>
    string PaneIcon => null;
}
