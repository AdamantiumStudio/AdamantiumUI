using System;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="ScrollViewer"/>: a pane that scrolls its content. Its scroll bars are parts of it,
/// not children.</summary>
public class ScrollViewerAutomationPeer : UIComponentAutomationPeer, IScrollProvider
{
    private const double Slack = 0.5;

    private readonly ScrollViewer _viewer;

    public ScrollViewerAutomationPeer(ScrollViewer owner) : base(owner)
    {
        _viewer = owner;
    }

    public override AutomationControlType ControlType => AutomationControlType.Pane;

    public bool HorizontallyScrollable => _viewer.ExtentSize.Width > _viewer.ViewportSize.Width + Slack;

    public bool VerticallyScrollable => _viewer.ExtentSize.Height > _viewer.ViewportSize.Height + Slack;

    public double HorizontalScrollPercent => HorizontallyScrollable
        ? Percent(_viewer.ScrollOffset.X, _viewer.ExtentSize.Width, _viewer.ViewportSize.Width)
        : IScrollProvider.NoScroll;

    public double VerticalScrollPercent => VerticallyScrollable
        ? Percent(_viewer.ScrollOffset.Y, _viewer.ExtentSize.Height, _viewer.ViewportSize.Height)
        : IScrollProvider.NoScroll;

    public double HorizontalViewSize => ViewSize(_viewer.ViewportSize.Width, _viewer.ExtentSize.Width);

    public double VerticalViewSize => ViewSize(_viewer.ViewportSize.Height, _viewer.ExtentSize.Height);

    public void SetScrollPercent(double horizontal, double vertical)
    {
        Check(horizontal, HorizontallyScrollable, "across");
        Check(vertical, VerticallyScrollable, "down");

        var offset = _viewer.ScrollOffset;
        var extent = _viewer.ExtentSize;
        var viewport = _viewer.ViewportSize;
        var x = horizontal == IScrollProvider.NoScroll ? offset.X : horizontal / 100 * (extent.Width - viewport.Width);
        var y = vertical == IScrollProvider.NoScroll ? offset.Y : vertical / 100 * (extent.Height - viewport.Height);
        _viewer.SetScrollOffset(new Vector2(x, y));
    }

    private void Check(double percent, bool scrollable, string axis)
    {
        if (percent == IScrollProvider.NoScroll)
        {
            return;
        }

        if (!scrollable)
        {
            throw new InvalidOperationException($"'{AutomationId}' does not scroll {axis}.");
        }

        if (percent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(percent), percent, "A scroll percent lies in 0 .. 100.");
        }
    }

    private static double Percent(double offset, double extent, double viewport) => offset / (extent - viewport) * 100;

    private static double ViewSize(double viewport, double extent) =>
        extent <= 0 ? 100 : Math.Min(100, viewport / extent * 100);
}
