using System;
using System.IO;
using Adamantium.UI.Core;

namespace Adamantium.UI.Input;

/// <summary>
/// Application-level drag-drop settings, separate from the user's <see cref="PlatformSettings"/>; set at startup.
/// </summary>
public static class DragDropOptions
{
    /// <summary>How long the pointer must dwell over a spring-loadable (a tab, a tree node) before it opens.
    /// Defaults to the user's own hover preference rather than a number of ours.</summary>
    public static double SpringLoadDelayMs { get; set; } = -1;

    /// <summary>How long the pointer must dwell over another of our windows before it is raised to the front.
    /// Same default, same reason.</summary>
    public static double WindowRaiseDelayMs { get; set; } = -1;

    /// <summary>Width of the edge band (px) where a drag starts auto-scrolling a scrollable area.</summary>
    public static double AutoScrollBand { get; set; } = 32;

    /// <summary>Auto-scroll speed (px/sec) for a scroll area that doesn't set <c>DragDrop.AutoScrollSpeed</c> itself.</summary>
    public static double AutoScrollSpeed { get; set; } = 450;

    /// <summary>How far below-right of the cursor the ghost sits, so it never hides the drop point.</summary>
    public static int GhostCursorOffset { get; set; } = 12;

    /// <summary>
    /// Also offers a dragged image as a file, written on demand into <see cref="ImageFileDirectory"/>, for targets that
    /// accept only files. Off by default because it writes to disk.
    /// </summary>
    public static bool OfferImagesAsFiles { get; set; }

    /// <summary>Where <see cref="OfferImagesAsFiles"/> puts its copies. Defaults to a folder of ours under the system
    /// temp directory - ours, because the retention sweep below deletes inside it.</summary>
    public static string ImageFileDirectory { get; set; } =
        Path.Combine(Path.GetTempPath(), "Adamantium", "drag");

    /// <summary>How long a dropped copy is kept before the next drag sweeps it away. Long enough that the target has
    /// certainly finished reading it, short enough that the folder does not grow forever. Zero disables the sweep.</summary>
    public static TimeSpan ImageFileRetention { get; set; } = TimeSpan.FromHours(1);

    /// <summary>The dwell values above, resolved: a negative value means "follow the user's hover setting".</summary>
    internal static double ResolveDwell(double configured) =>
        configured >= 0 ? configured : PlatformSettings.HoverTime;
}
