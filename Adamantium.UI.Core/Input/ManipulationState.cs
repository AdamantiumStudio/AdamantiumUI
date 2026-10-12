namespace Adamantium.UI.Core.Input;

/// <summary>Where a gesture of fingers is: recognized, going on, or over with its last finger lifted.</summary>
public enum ManipulationState
{
    Started,
    Delta,
    Completed
}
