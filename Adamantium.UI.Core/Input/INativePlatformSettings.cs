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
    Size<double> DoubleClickSize { get; }

    /// <summary>How long the pointer must rest before the OS calls it a HOVER, in milliseconds - the user's own dwell
    /// preference (Win32 <c>SPI_GETMOUSEHOVERTIME</c>, macOS the springing delay). Every "hold still and something
    /// opens" gesture should be paced by it rather than by a number we picked.</summary>
    uint HoverTime { get; }

    /// <summary>How far the pointer must travel, per axis, before a press counts as a DRAG rather than a click.
    /// Every desktop OS exposes this as a user setting (Win32 <c>SM_CXDRAG</c>/<c>SM_CYDRAG</c>, macOS's drag
    /// threshold, the GTK/Qt start-drag distance) - honoring it is the difference between "clicks sometimes
    /// drag by accident" and the app feeling like the rest of the system.</summary>
    Size<double> DragThreshold { get; }

    /// <summary>How long a held key waits before it repeats, in milliseconds (Win32 <c>SPI_GETKEYBOARDDELAY</c>, the
    /// Wayland seat's repeat delay).</summary>
    uint KeyRepeatDelay { get; }

    /// <summary>How long between two repeats of a held key, in milliseconds (Win32 <c>SPI_GETKEYBOARDSPEED</c>, the
    /// Wayland seat's repeat rate); 0 when the user turned repeating off.</summary>
    uint KeyRepeatInterval { get; }

    /// <summary>The languages the user set up in the OS, in the user's order, as language tags ("ru-RU") - Windows'
    /// language list, most wanted first; the desktop's input sources on Linux, in the order they switch. Read anew as the user changes them, cheaply enough to ask on
    /// every use; none where the platform does not say.</summary>
    IReadOnlyList<string> UserLanguages => [];
}
