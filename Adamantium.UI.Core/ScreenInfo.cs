namespace Adamantium.UI.Core;

/// <summary>One monitor, in physical desktop pixels.</summary>
public sealed class ScreenInfo
{
    public ScreenInfo(string id, Rect bounds, Rect workArea, double scale, bool isPrimary)
        : this(id, bounds, workArea, scale, isPrimary, 0)
    {
    }

    public ScreenInfo(string id, Rect bounds, Rect workArea, double scale, bool isPrimary, double refreshRate)
    {
        Id = id;
        Bounds = bounds;
        WorkArea = workArea;
        Scale = scale;
        IsPrimary = isPrimary;
        RefreshRate = refreshRate;
    }

    /// <summary>How many times a second the monitor shows a new picture; 0 when the platform does not say.</summary>
    public double RefreshRate { get; }

    /// <summary>What names the monitor from one run to the next, so a window can go back to it.</summary>
    public string Id { get; }

    public Rect Bounds { get; }

    /// <summary>The bounds without the taskbar and other docked bars: where windows are placed.</summary>
    public Rect WorkArea { get; }

    /// <summary>Physical pixels per logical unit on this monitor (1 at 100%).</summary>
    public double Scale { get; }

    public bool IsPrimary { get; }
}
