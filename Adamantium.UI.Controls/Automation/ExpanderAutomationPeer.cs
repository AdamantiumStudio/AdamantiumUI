using System;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of an <see cref="Expander"/>: a group called by its header, which opens and folds its content.</summary>
public class ExpanderAutomationPeer : UIComponentAutomationPeer, IExpandCollapseProvider
{
    private readonly Expander _expander;

    public ExpanderAutomationPeer(Expander owner) : base(owner)
    {
        _expander = owner;
    }

    public override AutomationControlType ControlType => AutomationControlType.Group;

    public ExpandCollapseState ExpandCollapseState =>
        _expander.IsExpanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

    public void Expand()
    {
        if (!_expander.IsExpanded)
        {
            _expander.Toggle();
        }
    }

    public void Collapse()
    {
        if (_expander.IsExpanded && !_expander.Toggle())
        {
            throw new InvalidOperationException($"'{AutomationId}' cannot fold.");
        }
    }

    protected override string NameCore() => _expander.Header as string ?? TextOf(_expander);
}
