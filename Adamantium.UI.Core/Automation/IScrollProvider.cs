namespace Adamantium.UI.Core.Automation;

/// <summary>An element that scrolls what it shows, such as a scroll viewer or a list. Positions are percents of the way
/// from the start to the end.</summary>
public interface IScrollProvider
{
    /// <summary>The percent that leaves an axis where it is, and that an axis which cannot scroll reports.</summary>
    const double NoScroll = -1;

    bool HorizontallyScrollable { get; }

    bool VerticallyScrollable { get; }

    double HorizontalScrollPercent { get; }

    double VerticalScrollPercent { get; }

    /// <summary>How much of the content is in view across, in percent.</summary>
    double HorizontalViewSize { get; }

    /// <summary>How much of the content is in view down, in percent.</summary>
    double VerticalViewSize { get; }

    /// <summary>Scrolls to the given percents; <see cref="NoScroll"/> leaves an axis where it is.</summary>
    void SetScrollPercent(double horizontal, double vertical);
}
