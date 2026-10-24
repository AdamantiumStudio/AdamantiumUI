using Adamantium.Mathematics;

namespace Adamantium.UI.Core;

/// <summary>The monitors the desktop shows windows on, as the platform tells them. Registered on
/// <see cref="Displays.Platform"/> at startup.</summary>
public interface IDisplayPlatform
{
    /// <summary>The monitors connected now, in physical pixels; empty when unknown.</summary>
    IReadOnlyList<ScreenInfo> Screens { get; }

    /// <summary>All monitors as one rectangle in physical pixels; empty when unknown.</summary>
    Rect VirtualScreen { get; }

    /// <summary>Whether a window can be put at a point of the desktop; Wayland places windows itself.</summary>
    bool CanPlaceWindows => true;
}
