using System.Collections.Generic;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="MenuHeader"/>: text naming the rows under it, as a gallery's group header is.</summary>
public class MenuHeaderAutomationPeer : UIComponentAutomationPeer
{
    public MenuHeaderAutomationPeer(MenuHeader owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.Text;

    protected override string NameCore() => (Owner as MenuHeader)?.Text;

    protected override IReadOnlyList<AutomationPeer> ChildrenCore() => [];
}
