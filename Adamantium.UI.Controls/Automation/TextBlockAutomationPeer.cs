using System.Collections.Generic;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="TextBlock"/>: text, called by what it says.</summary>
public class TextBlockAutomationPeer : UIComponentAutomationPeer
{
    public TextBlockAutomationPeer(TextBlock owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.Text;

    protected override string NameCore() => ((TextBlock)Owner).Text;

    protected override IReadOnlyList<AutomationPeer> ChildrenCore() => [];
}
