using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of an area that holds other elements - a drawing canvas, one of its floating panels.</summary>
public class PaneAutomationPeer : UIComponentAutomationPeer
{
    public PaneAutomationPeer(UIComponent owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.Pane;
}
