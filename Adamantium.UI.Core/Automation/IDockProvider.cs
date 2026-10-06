namespace Adamantium.UI.Core.Automation;

/// <summary>An element docked in a layout - a pane, a panel of panes - moved as a drag onto a dock target would move it.
/// </summary>
public interface IDockProvider
{
    DockPosition DockPosition { get; }

    /// <summary>Docks it against an edge, into the middle with the documents (<see cref="DockPosition.Fill"/>), or out
    /// into a window of its own (<see cref="DockPosition.None"/>) - by the layout's rules and the application's.</summary>
    void SetDockPosition(DockPosition position);

    /// <summary>Docks it against a side of the panel that holds <paramref name="target"/>, or into that panel as a tab for
    /// <see cref="DockPosition.Fill"/>. Not part of UI Automation's pattern, which knows only the edges of the whole.
    /// </summary>
    void DockBeside(AutomationPeer target, DockPosition side);
}
