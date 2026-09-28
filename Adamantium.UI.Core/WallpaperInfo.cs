using System;
using Adamantium.Mathematics;

namespace Adamantium.UI.Core;

/// <summary>A monitor's wallpaper: picture, fit and background color; <see cref="File"/> is null for a plain color. A
/// record compared as a whole; <see cref="Revision"/> catches a rewritten file at the same path.</summary>
public sealed record WallpaperInfo(Uri File, WallpaperFit Fit, Color Background, Rect MonitorBounds, DateTime Revision)
{
    /// <summary>What a platform returns when it cannot answer at all - no picture, no monitor. Distinguished from a
    /// plain-colour desktop by <see cref="MonitorBounds"/> being empty.</summary>
    public static readonly WallpaperInfo None = new(null, WallpaperFit.Fill, Colors.Transparent, default, default);

    /// <summary>Whether this says anything usable. A material asks before deciding between the wallpaper and its
    /// fallback - see the note on <see cref="DesktopWallpaper"/>.</summary>
    public bool IsKnown => MonitorBounds is { Width: > 0, Height: > 0 };
}
