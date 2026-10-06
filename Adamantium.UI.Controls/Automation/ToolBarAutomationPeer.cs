using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a strip of commands: a tool bar of what it holds.</summary>
public class ToolBarAutomationPeer : UIComponentAutomationPeer
{
    public ToolBarAutomationPeer(UIComponent owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.ToolBar;
}
