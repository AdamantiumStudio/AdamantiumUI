using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="StatusBar"/>: a status bar whose children are its fields.</summary>
public class StatusBarAutomationPeer : ItemsControlAutomationPeer
{
    public StatusBarAutomationPeer(StatusBar owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.StatusBar;

    protected override AutomationControlType ItemControlType => AutomationControlType.Text;
}
