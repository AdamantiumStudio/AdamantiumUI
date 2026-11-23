using System;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="GroupHeader"/>: a group named after its group, which folds and opens where its
/// header can.</summary>
public class GroupHeaderAutomationPeer : UIComponentAutomationPeer, IExpandCollapseProvider
{
    private readonly GroupHeader _header;

    public GroupHeaderAutomationPeer(GroupHeader owner) : base(owner)
    {
        _header = owner;
    }

    public override AutomationControlType ControlType => AutomationControlType.Group;

    public override object GetPattern(PatternId pattern) =>
        pattern == PatternId.ExpandCollapse && !_header.CanCollapse ? null : base.GetPattern(pattern);

    public ExpandCollapseState ExpandCollapseState =>
        _header.IsExpanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

    public void Expand()
    {
        if (!_header.IsExpanded && !_header.Toggle())
        {
            throw new InvalidOperationException($"'{AutomationId}' cannot open.");
        }
    }

    public void Collapse()
    {
        if (_header.IsExpanded && !_header.Toggle())
        {
            throw new InvalidOperationException($"'{AutomationId}' cannot fold.");
        }
    }
}
