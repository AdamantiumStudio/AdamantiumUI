using System.Collections.Generic;
using Adamantium.UI.Controls.DataGrid;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="DataGridColumnHeader"/>: a column's header, pressed to sort by the column as a click
/// on it sorts.</summary>
public class DataGridColumnHeaderAutomationPeer : ContentControlAutomationPeer, IInvokeProvider
{
    private readonly DataGridColumnHeader _header;

    public DataGridColumnHeaderAutomationPeer(DataGridColumnHeader owner) : base(owner)
    {
        _header = owner;
    }

    public override AutomationControlType ControlType => AutomationControlType.HeaderItem;

    public override object GetPattern(PatternId pattern) =>
        pattern == PatternId.Invoke && _header.Column is not { CanSort: true } ? null : base.GetPattern(pattern);

    public void Invoke() => _header.Owner?.SortByHeader(_header.Column);

    protected override string NameCore() => _header.Column?.Header as string ?? TextOf(_header);

    protected override IReadOnlyList<AutomationPeer> ChildrenCore() => [];
}
