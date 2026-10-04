namespace Adamantium.UI.Controls;

/// <summary>Where a window was when it closed: its screen, its place in that screen's work area, its size and whether it
/// was maximized. The place is kept as an offset from the work area, so the window finds its screen again wherever that
/// screen has been moved.</summary>
public sealed class WindowPlacement
{
    /// <summary>The <see cref="Core.ScreenInfo.Id"/> of the screen.</summary>
    public string ScreenId { get; set; }

    /// <summary>The window's left edge from the work area's, in physical pixels.</summary>
    public double Left { get; set; }

    /// <summary>The window's top edge from the work area's, in physical pixels.</summary>
    public double Top { get; set; }

    /// <summary>The client width in logical units, as the window was sized before it was maximized.</summary>
    public double ClientWidth { get; set; }

    /// <summary>The client height in logical units, as the window was sized before it was maximized.</summary>
    public double ClientHeight { get; set; }

    public bool IsMaximized { get; set; }
}
