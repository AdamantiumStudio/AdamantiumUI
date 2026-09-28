using System;

namespace Adamantium.UI.Core;

/// <summary>The desktop wallpaper per monitor, as the source for Mica: a file loaded and blurred once, re-read on change.
/// <see cref="Current"/> returns <see cref="WallpaperInfo.None"/> where the platform cannot tell.</summary>
public static class DesktopWallpaper
{
    /// <summary>The platform that answers, registered once at startup. Null on a platform with no implementation yet.</summary>
    public static IDesktopWallpaperPlatform Platform { get; set; }

    /// <summary>What the desktop shows on the monitor under <paramref name="point"/> (a DESKTOP point, physical - see
    /// <see cref="PixelPoint"/>), or <see cref="WallpaperInfo.None"/> when nothing can say.</summary>
    public static WallpaperInfo Current(PixelPoint point)
        => Platform?.GetWallpaper(point) ?? WallpaperInfo.None;

    /// <summary>Raised when the wallpaper changes. Not every change is announced, so holders also re-check
    /// <see cref="Current"/> on monitor or focus changes.</summary>
    public static event Action Changed;

    /// <summary>Called BY a platform when the OS reports a change. Public because the platform layer is a separate
    /// assembly - the same reason <see cref="Platform"/> is settable from outside.</summary>
    public static void RaiseChanged() => Changed?.Invoke();
}
