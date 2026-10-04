namespace Adamantium.UI.Core.Automation;

/// <summary>An element switched on and off, such as a check box.</summary>
public interface IToggleProvider
{
    ToggleState ToggleState { get; }

    /// <summary>Moves to the next state, the way a click does.</summary>
    void Toggle();
}
