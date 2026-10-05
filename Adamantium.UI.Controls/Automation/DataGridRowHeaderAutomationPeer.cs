using System.Collections.Generic;
using Adamantium.UI.Controls.DataGrid;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="DataGridRowHeader"/>: the header of a row, called by its number.</summary>
public class DataGridRowHeaderAutomationPeer : ContentControlAutomationPeer
{
    public DataGridRowHeaderAutomationPeer(DataGridRowHeader owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.HeaderItem;

    protected override string NameCore() => TextOf(Owner);

    protected override IReadOnlyList<AutomationPeer> ChildrenCore() => [];
}
