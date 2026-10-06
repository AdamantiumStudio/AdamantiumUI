using System;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="ZoomBox"/>: a pane zoomed as a whole and scrolled by its own scroll viewer.</summary>
public class ZoomBoxAutomationPeer : ContentControlAutomationPeer, ITransformProvider
{
    private readonly ZoomBox _box;
    private ScrollViewerAutomationPeer _scroll;

    public ZoomBoxAutomationPeer(ZoomBox owner) : base(owner)
    {
        _box = owner;
    }

    public override AutomationControlType ControlType => AutomationControlType.Pane;

    public bool CanMove => false;

    public bool CanResize => false;

    public bool CanZoom => true;

    public double ZoomLevel => _box.ScaleX * 100;

    public double ZoomMinimum => _box.MinScale * 100;

    public double ZoomMaximum => _box.MaxScale * 100;

    public void Move(double x, double y) => throw new InvalidOperationException("A zoom box is not moved.");

    public void Resize(double width, double height) => throw new InvalidOperationException("A zoom box is not resized.");

    public void Zoom(double percent) => _box.ZoomTo(percent / 100);

    public override object GetPattern(PatternId pattern) => pattern == PatternId.Scroll ? Scroll() : base.GetPattern(pattern);

    private ScrollViewerAutomationPeer Scroll()
    {
        if (_box.ScrollPart is not { } viewer)
        {
            return null;
        }

        if (_scroll?.Owner != viewer)
        {
            _scroll = new ScrollViewerAutomationPeer(viewer);
        }

        return _scroll;
    }
}
