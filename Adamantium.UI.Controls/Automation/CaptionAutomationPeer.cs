using System.Collections.Generic;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a content control that only says something - a total under a column, a group's caption: text,
/// called by what it shows.</summary>
public class CaptionAutomationPeer : ContentControlAutomationPeer
{
    public CaptionAutomationPeer(ContentControl owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.Text;

    protected override string NameCore() => base.NameCore() ?? TextOf(Owner);

    protected override IReadOnlyList<AutomationPeer> ChildrenCore() => [];
}
