namespace Adamantium.UI.Core.Input;

/// <summary>The keyboard's physical state right now from the OS, not as of the queued message; registered on
/// <see cref="Keyboard.Platform"/>.</summary>
public interface INativeKeyboard
{
    bool IsKeyDown(Key key);

    /// <summary>Is the key TOGGLED on (Caps Lock, Num Lock, Scroll Lock)?</summary>
    bool IsKeyToggled(Key key);

    /// <summary>Whether a keyboard layout the user switches between types a right-to-left script, as Hebrew or
    /// Arabic.</summary>
    bool HasRightToLeftLayout { get; }

    /// <summary>Whether <see cref="SendKey"/> and <see cref="SendText"/> reach another application: what an
    /// on-screen keyboard types into when the window with the keys is not one of ours. False unless a platform can.
    /// </summary>
    bool SendsToOtherApplications => false;

    /// <summary>The title of the window the system gives keys to now - another application's, or ours; null when
    /// the system does not say.</summary>
    string ForegroundWindowTitle => null;

    /// <summary>Which window the system gives keys to now: the same number while it is the same window, whatever its
    /// title says - an editor marking its document unsaved is still the window it was; 0 when the system does not say.
    /// A window closed may leave its number to one opened later.</summary>
    nint ForegroundWindowId => 0;

    /// <summary>Puts a key that types no text - Backspace, Enter, an arrow, a modifier, the C of Ctrl+C - into the
    /// system's input, going down or coming up, for the window the system gives keys to. False when it did not get
    /// in: a window of a process with more rights takes none, nor does a platform that cannot send. A key sent down
    /// is the sender's to send up - a modifier left down stays down for every application.</summary>
    bool SendKey(Key key, bool down) => false;

    /// <summary>Puts <paramref name="text"/> into the system's input as itself, whatever the keyboard layout, for the
    /// window the system gives keys to; a line break and a tab as their keys. False when it did not get in.</summary>
    bool SendText(string text) => false;
}
