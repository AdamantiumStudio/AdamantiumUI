using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="PropertyGrid"/>: a table of properties, in sections, with its search box.</summary>
public class PropertyGridAutomationPeer : UIComponentAutomationPeer
{
    public PropertyGridAutomationPeer(PropertyGrid owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.Table;
}
