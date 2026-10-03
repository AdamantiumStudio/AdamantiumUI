namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>What the layout buttons of the data grid page did last.</summary>
public enum LayoutAction
{
    None,
    Saved,
    Restored,
}

/// <summary>What the history buttons of the data grid page did last.</summary>
public enum HistoryStep
{
    NothingToUndo,
    Undone,
    Redone,
    NothingToRedo,
}

/// <summary>How the last export of the data grid page went.</summary>
public enum ExportState
{
    Nothing,
    NoDialog,
    Canceled,
    Written,
}
