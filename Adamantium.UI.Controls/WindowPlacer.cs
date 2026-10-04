using Adamantium.UI.Core;

namespace Adamantium.UI.Controls;

/// <summary>The arithmetic of putting a window on the screens, apart from any window. Every rectangle and size here is in
/// physical pixels.</summary>
public static class WindowPlacer
{
    /// <summary>Where a window of <paramref name="size"/> goes back to: its remembered screen, kept inside that screen's
    /// work area; null when the screen is no longer connected.</summary>
    public static PixelPoint? Restore(WindowPlacement placement, IReadOnlyList<ScreenInfo> screens, Size size)
    {
        var screen = screens.FirstOrDefault(s => s.Id == placement.ScreenId);
        if (screen == null)
        {
            return null;
        }

        var area = screen.WorkArea;
        return new PixelPoint(
            Math.Clamp(area.X + placement.Left, area.X, Math.Max(area.X, area.Right - size.Width)),
            Math.Clamp(area.Y + placement.Top, area.Y, Math.Max(area.Y, area.Bottom - size.Height)));
    }

    /// <summary>The top-left that puts a window of <paramref name="size"/> in the middle of <paramref name="area"/>, its
    /// caption never above the area's top.</summary>
    public static PixelPoint Center(Rect area, Size size) =>
        new(area.X + (area.Width - size.Width) / 2, Math.Max(area.Y, area.Y + (area.Height - size.Height) / 2));

    /// <summary>The screen that holds most of <paramref name="bounds"/>; the primary one when none holds any.</summary>
    public static ScreenInfo ScreenOf(Rect bounds, IReadOnlyList<ScreenInfo> screens)
    {
        ScreenInfo best = null;
        double bestOverlap = 0;
        foreach (var screen in screens)
        {
            var overlap = Overlap(bounds, screen.Bounds);
            if (overlap > bestOverlap)
            {
                best = screen;
                bestOverlap = overlap;
            }
        }

        return best ?? Primary(screens);
    }

    /// <summary>The screen that holds <paramref name="point"/>; the primary one when none does.</summary>
    public static ScreenInfo ScreenAt(PixelPoint point, IReadOnlyList<ScreenInfo> screens) =>
        screens.FirstOrDefault(s => point.X >= s.Bounds.X && point.X < s.Bounds.Right && point.Y >= s.Bounds.Y && point.Y < s.Bounds.Bottom)
        ?? Primary(screens);

    private static ScreenInfo Primary(IReadOnlyList<ScreenInfo> screens) => screens.FirstOrDefault(s => s.IsPrimary) ?? screens.FirstOrDefault();

    private static double Overlap(Rect a, Rect b)
    {
        var width = Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X);
        var height = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y);
        return width > 0 && height > 0 ? width * height : 0;
    }
}
