using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Input;

/// <summary>Where the pointer is while the platform carries a window drag and desktop points do not relate windows
/// (Wayland): the window under it and the logical point of that window. Set and read on the loop thread.</summary>
public static class WindowDragPointer
{
    /// <summary>Whether the platform reports the pointer this way for the drag in progress.</summary>
    public static bool IsActive { get; private set; }

    /// <summary>The window the pointer is over; null over none of ours.</summary>
    public static IWindow Window { get; private set; }

    /// <summary>The pointer's logical point in <see cref="Window"/>.</summary>
    public static Vector2<double> Position { get; private set; }

    public static void Begin()
    {
        IsActive = true;
        Window = null;
    }

    public static void Update(IWindow window, Vector2<double> position)
    {
        Window = window;
        Position = position;
    }

    public static void End()
    {
        IsActive = false;
        Window = null;
    }
}
