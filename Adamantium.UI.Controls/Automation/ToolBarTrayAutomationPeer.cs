using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a tray of tool bars: a pane holding them.</summary>
public class ToolBarTrayAutomationPeer : UIComponentAutomationPeer
{
    public ToolBarTrayAutomationPeer(UIComponent owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.Pane;
}
