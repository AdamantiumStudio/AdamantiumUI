using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="RibbonQuickAccess"/>: a toolbar of the commands put in it, then its overflow button.</summary>
public class RibbonQuickAccessAutomationPeer : UIComponentAutomationPeer
{
    public RibbonQuickAccessAutomationPeer(RibbonQuickAccess owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.ToolBar;
}
