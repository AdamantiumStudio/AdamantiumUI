namespace Adamantium.UI.Core.Automation;

/// <summary>A view of a plane with no edge - a canvas, a fractal - whose content is dragged across it. Automation's own
/// capability: UI Automation's Scroll pattern needs edges to say how far along it is, and Transform moves the element,
/// not what it shows.</summary>
public interface IPanProvider
{
    /// <summary>Moves what the view shows by <paramref name="dx"/>, <paramref name="dy"/> of the view's own units, as a
    /// drag that far would: the content follows.</summary>
    void Pan(double dx, double dy);
}
