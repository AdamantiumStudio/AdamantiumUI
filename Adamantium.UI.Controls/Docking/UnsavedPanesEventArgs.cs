namespace Adamantium.UI.Controls.Docking;

/// <summary>Panes with unsaved work about to close: set <see cref="Answer"/> to answer for the area, or leave it and the
/// area asks.</summary>
public class UnsavedClosingEventArgs : EventArgs
{
    public UnsavedClosingEventArgs(IReadOnlyList<string> paneIds) => PaneIds = paneIds;

    public IReadOnlyList<string> PaneIds { get; }

    public UnsavedAnswer Answer { get; set; }
}

/// <summary>Panes to save before they close. A handler saves them and sets <see cref="Failed"/> for what it could not -
/// then nothing closes.</summary>
public class PanesSavingEventArgs : EventArgs
{
    public PanesSavingEventArgs(IReadOnlyList<string> paneIds) => PaneIds = paneIds;

    public IReadOnlyList<string> PaneIds { get; }

    public bool Failed { get; set; }
}
