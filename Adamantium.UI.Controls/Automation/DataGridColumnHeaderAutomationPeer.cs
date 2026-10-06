using System.Collections.Generic;
using Adamantium.UI.Controls.DataGrid;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="DataGridColumnHeader"/>: a column's header, pressed to sort by the column as a click
/// on it sorts; its funnel, while the column can be filtered, is a button that opens the filter.</summary>
public class DataGridColumnHeaderAutomationPeer : ContentControlAutomationPeer, IInvokeProvider
{
    private readonly DataGridColumnHeader _header;
    private PartButtonAutomationPeer _filter;

    public DataGridColumnHeaderAutomationPeer(DataGridColumnHeader owner) : base(owner)
    {
        _header = owner;
    }

    public override AutomationControlType ControlType => AutomationControlType.HeaderItem;

    public override object GetPattern(PatternId pattern) =>
        pattern == PatternId.Invoke && _header.Column is not { CanSort: true } ? null : base.GetPattern(pattern);

    public void Invoke() => _header.Owner?.SortByHeader(_header.Column);

    protected override string NameCore() => _header.Column?.Header as string ?? TextOf(_header);

    protected override IReadOnlyList<AutomationPeer> ChildrenCore()
    {
        if (_header.FilterPart is not { } funnel)
        {
            return [];
        }

        if (_filter?.Owner != funnel)
        {
            _filter = new PartButtonAutomationPeer(funnel, () => _header.PressFilter());
        }

        return [_filter];
    }
}
