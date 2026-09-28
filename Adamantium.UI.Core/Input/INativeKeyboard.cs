namespace Adamantium.UI.Core.Input;

/// <summary>The keyboard's physical state right now from the OS, not as of the queued message; registered on
/// <see cref="Keyboard.Platform"/>.</summary>
public interface INativeKeyboard
{
    bool IsKeyDown(Key key);

    /// <summary>Is the key TOGGLED on (Caps Lock, Num Lock, Scroll Lock)?</summary>
    bool IsKeyToggled(Key key);
}
