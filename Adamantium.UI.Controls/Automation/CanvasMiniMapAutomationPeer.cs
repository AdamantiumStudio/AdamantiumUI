using System.Collections.Generic;
using Adamantium.UI.Controls.DrawingBoard;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="CanvasMiniMap"/>: a picture of the whole plane, called by the name it is given.
/// Pressing it moves the camera; automation moves the camera on the canvas itself - panned, or a node brought into
/// view.</summary>
public class CanvasMiniMapAutomationPeer : UIComponentAutomationPeer
{
    public CanvasMiniMapAutomationPeer(CanvasMiniMap owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.Image;

    protected override IReadOnlyList<AutomationPeer> ChildrenCore() => [];
}
