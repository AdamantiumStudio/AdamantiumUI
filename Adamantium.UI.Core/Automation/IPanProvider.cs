namespace Adamantium.UI.Core.Automation;

/// <summary>A view whose content is dragged across it - a canvas, a fractal, a zoom box. Automation's own capability:
/// UI Automation's Scroll pattern speaks in percents of edges a plane may not have, and Transform moves the element,
/// not what it shows.</summary>
public interface IPanProvider
{
    /// <summary>Moves what the view shows by <paramref name="dx"/>, <paramref name="dy"/> of the view's own units, as a
    /// drag that far would: the content follows.</summary>
    void Pan(double dx, double dy);
}
