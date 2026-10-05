using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="DataPager"/>: a group of the buttons, the page number and the page size it turns
/// pages with.</summary>
public class DataPagerAutomationPeer : UIComponentAutomationPeer
{
    public DataPagerAutomationPeer(DataPager owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.Group;
}
