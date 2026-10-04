namespace Adamantium.Navigation;

/// <summary>Where a docked pane is, whether it can be seen and whether it is the one being worked in. The default value -
/// zone <see cref="DockZone.None"/> - is a pane that is not there.</summary>
public readonly struct PanePlacement
{
    public PanePlacement(DockZone zone, PaneState state, bool isActive, bool isShown)
    {
        Zone = zone;
        State = state;
        IsActive = isActive;
        IsShown = isShown;
    }

    /// <summary>The side of the documents, <see cref="DockZone.Center"/> among them, <see cref="DockZone.Floating"/> in a
    /// window of its own. For a hidden tool, where it was.</summary>
    public DockZone Zone { get; }

    public PaneState State { get; }

    public bool IsActive { get; }

    /// <summary>On screen: the front tab of a panel that is not folded away.</summary>
    public bool IsShown { get; }

    public bool IsFloating => Zone == DockZone.Floating;

    /// <summary>Whether the pane is somewhere else or folded otherwise; becoming active or coming to the front is not a
    /// move.</summary>
    public bool IsMovedFrom(PanePlacement other) => Zone != other.Zone || State != other.State;
}
