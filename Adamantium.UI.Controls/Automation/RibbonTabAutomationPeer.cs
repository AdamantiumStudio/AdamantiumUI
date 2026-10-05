using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="RibbonTab"/>: the open tab's pane, called by its header, whose children are its
/// groups.</summary>
public class RibbonTabAutomationPeer : ItemsControlAutomationPeer
{
    public RibbonTabAutomationPeer(RibbonTab owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.Pane;

    protected override AutomationControlType ItemControlType => AutomationControlType.Group;

    protected override string NameCore() => ((RibbonTab)Owner).Header as string;
}
