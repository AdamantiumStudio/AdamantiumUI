using System;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a splitter: a thumb moved along its axis, which moves the boundary between its neighbors.</summary>
public class SplitterAutomationPeer : ThumbAutomationPeer, ITransformProvider
{
    private readonly ISplitter _splitter;

    internal SplitterAutomationPeer(Thumb owner, ISplitter splitter) : base(owner)
    {
        _splitter = splitter;
    }

    public bool CanMove => _splitter.CanMoveSplit;

    public bool CanResize => false;

    public bool CanZoom => false;

    public double ZoomLevel => 100;

    public double ZoomMinimum => 100;

    public double ZoomMaximum => 100;

    public void Move(double x, double y)
    {
        if (!CanMove)
        {
            throw new InvalidOperationException("The splitter has no two neighbors to move the boundary between.");
        }

        var bounds = BoundingRectangle;
        var delta = _splitter.MovesAcross
            ? (x - bounds.X) * Owner.RenderSize.Width / bounds.Width
            : (y - bounds.Y) * Owner.RenderSize.Height / bounds.Height;
        _splitter.MoveSplit(delta);
    }

    public void Resize(double width, double height) => throw new InvalidOperationException("A splitter is not resized.");

    public void Zoom(double percent) => throw new InvalidOperationException("A splitter is not zoomed.");
}
