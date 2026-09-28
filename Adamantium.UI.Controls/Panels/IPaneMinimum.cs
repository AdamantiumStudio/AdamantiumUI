namespace Adamantium.UI.Controls.Panels;

/// <summary>Reports how small an element may become along an axis; splitters and pane hosts ask before squeezing it.</summary>
public interface IPaneMinimum
{
    /// <summary>Smallest extent in pixels along <paramref name="orientation"/>, or 0 for "no opinion".</summary>
    double MinimumExtent(Orientation orientation);
}
