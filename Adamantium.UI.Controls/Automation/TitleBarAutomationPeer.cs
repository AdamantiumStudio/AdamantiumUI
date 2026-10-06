using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="TitleBar"/>: called by the title, holding the caption buttons.</summary>
public class TitleBarAutomationPeer : UIComponentAutomationPeer
{
    public TitleBarAutomationPeer(TitleBar owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.TitleBar;

    protected override string NameCore() => ((TitleBar)Owner).Title;
}
