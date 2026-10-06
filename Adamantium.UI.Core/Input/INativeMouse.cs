using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Input;

/// <summary>
/// Live pointer state straight from the OS, for the two things our own message-driven tracking cannot answer: where the
/// pointer is when no move has reached us (another application owns it, or nothing moved since we last looked), and
/// warping it somewhere. A platform registers its implementation on <see cref="Mouse.Platform"/> at startup.
/// </summary>
public interface INativeMouse
{
    /// <summary>Where the pointer is on the DESKTOP - see <see cref="PixelPoint"/> for why that has a type of its own.</summary>
    PixelPoint Position { get; set; }

    /// <summary>The window a gesture made inside the application is in - null for the system pointer, whose window is
    /// whichever the desktop has on top at <see cref="Position"/>. A made gesture is in the window it was made in, whatever
    /// another application has over it.</summary>
    IWindow Window => null;
}
