using System.Collections.Generic;
using System.Linq;
using Adamantium.UI.Controls.DataGrid;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="DataGridRowDetailsToggle"/>: a button that opens and shuts its row's details panel.
/// A blank one, on a row with no panel, can do nothing.</summary>
public class DataGridRowDetailsToggleAutomationPeer : ContentControlAutomationPeer, IExpandCollapseProvider
{
    private readonly DataGridRowDetailsToggle _toggle;

    public DataGridRowDetailsToggleAutomationPeer(DataGridRowDetailsToggle owner) : base(owner)
    {
        _toggle = owner;
    }

    public override AutomationControlType ControlType => AutomationControlType.Button;

    public ExpandCollapseState ExpandCollapseState =>
        _toggle.IsOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

    public override object GetPattern(PatternId pattern) =>
        pattern == PatternId.ExpandCollapse && _toggle.IsBlank ? null : base.GetPattern(pattern);

    public void Expand()
    {
        if (!_toggle.IsOpen)
        {
            Toggle();
        }
    }

    public void Collapse()
    {
        if (_toggle.IsOpen)
        {
            Toggle();
        }
    }

    protected override IReadOnlyList<AutomationPeer> ChildrenCore() => [];

    private void Toggle()
    {
        if (_toggle.GetVisualAncestors().OfType<DataGridRow>().FirstOrDefault() is { Owner: { } grid, Item: var item })
        {
            grid.ToggleRowDetails(item);
        }
    }
}
