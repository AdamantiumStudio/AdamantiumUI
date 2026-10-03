namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>What the docking page's application last answered when the area asked it.</summary>
public enum DockingAnswer
{
    NothingAsked,
    Refused,
    UserClosed,
    UserKept,
    Unsaved,
    Saved,
    Asks,
    DoesNotAsk,
    OwnRow,
    SharedRow,
    PanelTornOff,
    TabTornOff,
    Docked,
}

/// <summary>What became of the docking page's saved layout last.</summary>
public enum DockingLayoutReport
{
    Pending,
    NoSavedLayout,
    RestoredFromFile,
    Unreadable,
    NotOnScreen,
    SavedToFile,
    NothingSaved,
    Restored,
    NamesNothing,
    Forgotten,
}
