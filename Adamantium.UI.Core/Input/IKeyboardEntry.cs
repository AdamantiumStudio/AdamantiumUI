namespace Adamantium.UI.Core.Input;

/// <summary>A container that Tab enters once and that says itself where: a list enters at the row the keyboard was on,
/// which the navigator's own memory of the last focused element cannot know once that element shows another row.</summary>
public interface IKeyboardEntry
{
    /// <summary>Where Tab enters, or null to leave it to the navigator.</summary>
    IInputComponent KeyboardEntry();
}
