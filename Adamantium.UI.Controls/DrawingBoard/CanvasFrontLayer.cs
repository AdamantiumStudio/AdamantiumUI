using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core.Graphics;

namespace Adamantium.UI.Controls.DrawingBoard;

/// <summary>The glass over the plane: the half-made shape under the pen and the pointer's marks. The selection frame is drawn
/// with its object's layer. Draw-only.</summary>
public class CanvasFrontLayer : MeasurableUIComponent
{
    /// <summary>The canvas whose items this draws. Written by the canvas when it takes the part.</summary>
    public InfiniteCanvas Owner { get; set; }

    public CanvasFrontLayer()
    {
        IsHitTestVisible = false;
    }

    protected override void OnRender(IDrawingContext context)
    {
        base.OnRender(context);

        if (Owner == null) return;

        var session = context.ForControl(this);

        Owner.DrawInProgress(session);

        // The frame only reaches here when nothing on the plane is drawn over what is held - otherwise it is drawn by
        // the layer that holds it, which is what puts it in the order with its object.
        if (Owner.ChromeGoesOnGlass) Owner.DrawChrome(session);

        // The pointer's own marks always: they say where the HAND is, not where anything stands, and a plate read
        // through the drawing it is measuring is no plate at all.
        Owner.DrawPointer(session);
    }
}
