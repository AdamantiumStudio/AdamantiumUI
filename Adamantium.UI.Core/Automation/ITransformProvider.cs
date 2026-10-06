namespace Adamantium.UI.Core.Automation;

/// <summary>An element that is moved, resized or zoomed as a whole: a splitter dragged along, a node placed on a canvas,
/// a zoom box scaled. Places and sizes are screen pixels, as <see cref="AutomationPeer.BoundingRectangle"/> reports them;
/// zoom levels are percents.</summary>
public interface ITransformProvider
{
    bool CanMove { get; }

    bool CanResize { get; }

    bool CanZoom { get; }

    /// <summary>The zoom in percent; 100 shows the content at its own size.</summary>
    double ZoomLevel { get; }

    double ZoomMinimum { get; }

    double ZoomMaximum { get; }

    /// <summary>Moves the element so its top-left corner is at <paramref name="x"/>, <paramref name="y"/>. An element
    /// that moves along one axis only keeps the other.</summary>
    void Move(double x, double y);

    void Resize(double width, double height);

    /// <summary>Zooms to <paramref name="percent"/>, kept between the minimum and the maximum.</summary>
    void Zoom(double percent);
}
