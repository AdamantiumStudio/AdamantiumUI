using System.Collections.Generic;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="CalendarMonth"/>: a group of days called by the month's name.</summary>
public class CalendarMonthAutomationPeer : UIComponentAutomationPeer
{
    public CalendarMonthAutomationPeer(CalendarMonth owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.Group;

    protected override string NameCore() => base.NameCore() ?? ((CalendarMonth)Owner).Title;

    protected override IReadOnlyList<AutomationPeer> ChildrenCore() => CalendarAutomation.Shown(base.ChildrenCore(), Owner);
}
