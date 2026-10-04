using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a window: the root of its tree, called by its title, and found by its class name unless it is
/// given an id or a name.</summary>
public class WindowAutomationPeer : UIComponentAutomationPeer
{
    public WindowAutomationPeer(WindowBase owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.Window;

    public override string AutomationId => base.AutomationId is { Length: > 0 } id ? id : ClassName;

    protected override string NameCore() => ((WindowBase)Owner).Title;
}
