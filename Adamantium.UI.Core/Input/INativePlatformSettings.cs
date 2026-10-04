using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Input;

/// <summary>
/// User-configured input settings owned by the OS, not by us - honoring them is what makes the app feel native.
/// Registered on <see cref="PlatformSettings.Platform"/> at startup.
/// </summary>
public interface INativePlatformSettings
{
    /// <summary>Longest gap between two clicks that still counts as a double-click, in milliseconds.</summary>
    uint DoubleClickTime { get; }

    /// <summary>How far apart two clicks may land and still be one double-click, per axis (Win32
    /// <c>SM_CXDOUBLECLK</c>/<c>SM_CYDOUBLECLK</c>). A double click is two clicks in the SAME PLACE, not merely two in
    /// quick succession - without this a run of quick clicks anywhere on a surface keeps counting up, and any gesture
    /// that means something on the second one fires where nobody asked for it.</summary>
    Size DoubleClickSize { get; }

    /// <summary>How long the pointer must rest before the OS calls it a HOVER, in milliseconds - the user's own dwell
    /// preference (Win32 <c>SPI_GETMOUSEHOVERTIME</c>, macOS the springing delay). Every "hold still and something
    /// opens" gesture should be paced by it rather than by a number we picked.</summary>
    uint HoverTime { get; }

    /// <summary>How far the pointer must travel, per axis, before a press counts as a DRAG rather than a click.
    /// Every desktop OS exposes this as a user setting (Win32 <c>SM_CXDRAG</c>/<c>SM_CYDRAG</c>, macOS's drag
    /// threshold, the GTK/Qt start-drag distance) - honoring it is the difference between "clicks sometimes
    /// drag by accident" and the app feeling like the rest of the system.</summary>
    Size DragThreshold { get; }

    /// <summary>All monitors as one rectangle in physical pixels, for checking that a saved window position is still
    /// reachable; empty when unknown.</summary>
    Rect VirtualScreen { get; }

    /// <summary>The monitors connected now; empty when unknown.</summary>
    IReadOnlyList<ScreenInfo> Screens { get; }
}
