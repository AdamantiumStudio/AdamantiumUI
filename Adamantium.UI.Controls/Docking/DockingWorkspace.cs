using System;
using System.Threading.Tasks;

namespace Adamantium.UI.Controls.Docking;

/// <summary>The handle a view model keeps for the docking arrangement, calling <see cref="Save"/> and <see cref="Load"/>
/// itself; where the layout is stored stays the application's business.</summary>
public class DockingWorkspace
{
    private DockingArea _area;

    /// <summary>Raised after a layout has been applied - the view model may want to write down that it worked, or
    /// enable the commands that only make sense once there is an arrangement.</summary>
    public event EventHandler Restored;

    /// <summary>A pane is about to close - set <see cref="PaneClosingEventArgs.Cancel"/> to refuse. The QUESTION comes
    /// from the control (a close can be asked for by the tab's own button as much as by a menu), and the ANSWER belongs
    /// to whoever owns the document's state, which is the view model. This is how it reaches one: a view model cannot
    /// see the area, and the area must not go looking for state inside the visual tree.</summary>
    public event Func<object, PaneClosingEventArgs, Task> PaneClosing;

    /// <summary>Raised after a pane has closed - for anything keeping its own list of what is open.</summary>
    public event EventHandler<PaneClosedEventArgs> PaneClosed;

    /// <summary>Raised when the view is built and an area has attached itself. This is when a view model may restore a
    /// saved arrangement: before it there is nothing to restore INTO, and a view model has no other way to know - it
    /// deliberately cannot see the view.</summary>
    public event EventHandler Ready;

    /// <summary>Whether an area is attached. False before the view is built, which is exactly when a view model would
    /// otherwise try to restore a layout into nothing.</summary>
    public bool IsReady => _area != null;

    /// <summary>The whole arrangement as text, or null when no area is attached yet.</summary>
    public string Save() => _area?.SaveLayout();

    /// <summary>Applies a saved arrangement. False when there is no area yet, or the text is not one this version can
    /// read - the caller keeps what is on screen, which on a first run is the authored arrangement.</summary>
    public bool Load(string state)
    {
        if (_area == null || !_area.LoadLayout(state)) return false;

        Restored?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Where pinned tabs live in every panel of the area - a row of their own or the one row. Null before an
    /// area has attached, and a write then is simply dropped: there is nothing yet to arrange.</summary>
    public PinnedTabsPlacement? PinnedTabsPlacement
    {
        get => _area?.PinnedTabsPlacement;
        set { if (_area != null) _area.PinnedTabsPlacement = value; }
    }

    // Closing is NOT proxied here. A menu, a toolbar button, anything standing next to the control has the area in
    // hand and calls DockingArea.ClosePane / CloseOtherPanes / … directly; passing that through the workspace would be
    // indirection with nothing in it. What a view model genuinely cannot do without is the QUESTION above - it has no
    // way to reach the area to subscribe.

    // Called by the attached property when the view is built. A workspace serves ONE area: two would make "save" mean
    // two different arrangements under one name.
    internal void Attach(DockingArea area)
    {
        // One area at a time: release the outgoing area's floating windows and carry the arrangement to the new one, or each
        // re-entry stacks windows and resets the layout.
        string carried = null;
        if (_area != null && !ReferenceEquals(_area, area))
        {
            carried = _area.SaveLayout();
            _area.PaneClosing -= OnPaneClosing;
            _area.PaneClosed -= OnPaneClosed;
            _area.ReleaseFloatingWindows();
        }

        _area = area;
        area.PaneClosing += OnPaneClosing;
        area.PaneClosed += OnPaneClosed;

        // AFTER the wiring, so a pane the saved tree names and this area has not got is asked for through the events
        // the new area is now subscribed to (DockingArea.LoadLayout raises PaneRestoreRequested for exactly those).
        if (!string.IsNullOrEmpty(carried)) area.LoadLayout(carried);

        Ready?.Invoke(this, EventArgs.Empty);
    }

    internal void Detach(DockingArea area)
    {
        area.PaneClosing -= OnPaneClosing;
        area.PaneClosed -= OnPaneClosed;
        if (ReferenceEquals(_area, area)) _area = null;
    }

    // Forwarded verbatim, refusal and WAITING included: the workspace decides nothing, it only lets the two sides talk.
    private Task OnPaneClosing(object sender, PaneClosingEventArgs e) =>
        PaneClosing?.Invoke(this, e) ?? Task.CompletedTask;

    private void OnPaneClosed(object sender, PaneClosedEventArgs e) => PaneClosed?.Invoke(this, e);
}
