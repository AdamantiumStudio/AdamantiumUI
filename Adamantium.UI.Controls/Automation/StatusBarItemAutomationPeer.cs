using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="StatusBarItem"/>: a button when a press does something, text when it only tells.</summary>
public class StatusBarItemAutomationPeer : ButtonAutomationPeer
{
    public StatusBarItemAutomationPeer(StatusBarItem owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType =>
        ((StatusBarItem)Owner).IsInteractive ? AutomationControlType.Button : AutomationControlType.Text;

    public override object GetPattern(PatternId pattern) =>
        pattern == PatternId.Invoke && !((StatusBarItem)Owner).IsInteractive ? null : base.GetPattern(pattern);
}
