using Adamantium.Mathematics;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.DrawingBoard;

/// <summary>The disc on a <see cref="CanvasNode"/>'s edge a connection docks into; a type of its own so a socket can be
/// found under the pointer. No behavior.</summary>
public class CanvasNodeSocket : Border
{
    /// <summary>The socket this disc stands for - its own <see cref="DataContext"/>, said out loud so that whoever
    /// found the disc does not have to know how the template was wired.</summary>
    public CanvasNodePin Pin => DataContext as CanvasNodePin;

    /// <summary>The middle of it, in the coordinates of <paramref name="within"/> - the node, usually. Null while the
    /// node has not been laid out, which is every frame before the first one.</summary>
    public Vector2? MiddleIn(IUIComponent within)
    {
        if (within == null || RenderSize.Width <= 0 || RenderSize.Height <= 0) return null;

        return this.TranslatePoint(new Vector2(RenderSize.Width / 2, RenderSize.Height / 2), within);
    }

    protected override AutomationPeer OnCreateAutomationPeer() =>
        TemplatedParent is CanvasNode node && node.IsStub(this) ? null : new CanvasSocketAutomationPeer(this);
}
