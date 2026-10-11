namespace Adamantium.UI.Core.Input;

/// <summary>Where a long press is: the contact has rested long enough, has lifted after that, or moved off or was taken
/// away before it lifted.</summary>
public enum HoldingState
{
    Started,
    Completed,
    Canceled
}
