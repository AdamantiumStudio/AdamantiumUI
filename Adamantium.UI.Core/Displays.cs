using Adamantium.Mathematics;

namespace Adamantium.UI.Core;

/// <summary>The monitors the desktop shows windows on.</summary>
public static class Displays
{
    /// <summary>The platform that tells them, registered once at startup; null when it does not.</summary>
    public static IDisplayPlatform Platform { get; set; }

    /// <summary>The monitors connected now, in physical pixels; empty when the platform does not say.</summary>
    public static IReadOnlyList<ScreenInfo> Screens => Platform?.Screens ?? [];

    /// <summary>Every monitor as one rectangle, in physical pixels, or an empty one when the platform does not say.
    /// Used to check that a remembered window position still exists - see <see cref="IsOnScreen"/>.</summary>
    public static Rect VirtualScreen => Platform?.VirtualScreen ?? default;

    /// <summary>Whether a window can be put at a point of the desktop. Where it cannot, an overlay is drawn inside the
    /// window it covers.</summary>
    public static bool CanPlaceWindows => Platform?.CanPlaceWindows ?? true;

    /// <summary>Whether a saved window rectangle is still reachable: its top-left and a grabbable caption strip are on a
    /// monitor. True when the platform cannot tell.</summary>
    public static bool IsOnScreen(Rect bounds)
    {
        var screen = VirtualScreen;
        if (screen.Width <= 0 || screen.Height <= 0)
        {
            return true;
        }

        const double grabbable = 48;
        return bounds.X + bounds.Width - grabbable > screen.X
               && bounds.X + grabbable < screen.X + screen.Width
               && bounds.Y + grabbable < screen.Y + screen.Height
               && bounds.Y + bounds.Height > screen.Y;
    }
}
