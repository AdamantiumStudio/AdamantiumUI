using System;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.DrawingBoard;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="CanvasPane"/>: a panel floating over a canvas, moved as a drag by its grip moves it
/// and widened as a pull on its edge does.</summary>
public class CanvasPaneAutomationPeer : PaneAutomationPeer, ITransformProvider
{
    private readonly CanvasPane _pane;

    public CanvasPaneAutomationPeer(CanvasPane owner) : base(owner)
    {
        _pane = owner;
    }

    public bool CanMove => _pane.CanDrag;

    public bool CanResize => _pane.CanResize;

    public bool CanZoom => false;

    public double ZoomLevel => 100;

    public double ZoomMinimum => 100;

    public double ZoomMaximum => 100;

    public void Move(double x, double y)
    {
        if (!CanMove)
        {
            throw new InvalidOperationException($"'{AutomationId}' does not move.");
        }

        var bounds = BoundingRectangle;
        var units = UnitsPerPixel();
        _pane.MoveBy(new Vector2((x - bounds.X) * units.X, (y - bounds.Y) * units.Y));
    }

    /// <summary>Widens or narrows it; its height follows what it holds.</summary>
    public void Resize(double width, double height)
    {
        if (!CanResize)
        {
            throw new InvalidOperationException($"'{AutomationId}' does not resize.");
        }

        _pane.ResizeTo(width * UnitsPerPixel().X);
    }

    public void Zoom(double percent) => throw new InvalidOperationException("A panel is not zoomed.");
}
