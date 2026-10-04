namespace Adamantium.UI.Controls.Docking;

/// <summary>A pane's id in a layout saved by an older version of the application: set <see cref="NewId"/> to the id it
/// has now. One that names no pane any more is dropped.</summary>
public class PaneMigratingEventArgs : EventArgs
{
    public PaneMigratingEventArgs(int savedVersion, string paneId)
    {
        SavedVersion = savedVersion;
        PaneId = paneId;
        NewId = paneId;
    }

    /// <summary>The <see cref="DockingArea.LayoutVersion"/> the layout was saved with.</summary>
    public int SavedVersion { get; }

    public string PaneId { get; }

    public string NewId { get; set; }
}

/// <summary>A pane's own state - what it scrolled to, which of its tabs was open - written into a saved layout and given
/// back when it is loaded.</summary>
public class PaneStateEventArgs : EventArgs
{
    public PaneStateEventArgs(string paneId, string state)
    {
        PaneId = paneId;
        State = state;
    }

    public string PaneId { get; }

    /// <summary>Set it when saving; read it when restoring.</summary>
    public string State { get; set; }
}
