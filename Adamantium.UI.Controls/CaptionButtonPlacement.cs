namespace Adamantium.UI.Controls;

/// <summary>Which side of the title bar the window buttons sit on - a platform convention (Windows right, macOS left), so
/// the theme states it.</summary>
public enum CaptionButtonPlacement
{
    /// <summary>Right of the title, closing outermost. Windows and most Linux desktops.</summary>
    Right,

    /// <summary>Left of the title, closing outermost - so the order reverses to close, minimize, maximize. macOS, and
    /// Ubuntu since Unity.</summary>
    Left
}
