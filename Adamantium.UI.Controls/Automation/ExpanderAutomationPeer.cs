using System;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of an <see cref="Expander"/>: a group called by its header, which opens and folds its content, and
/// is scrolled into view where it stands in something that scrolls - a section of a property grid.</summary>
public class ExpanderAutomationPeer : UIComponentAutomationPeer, IExpandCollapseProvider, IScrollItemProvider
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

    // Its header, where the group starts, as a user scrolls to a group - the whole of one taller than the view would show
    // its end. One move: a second one, before the layout has caught up, would be measured from where it was.
    public void ScrollIntoView() => ((_expander.HeaderPart as Base.UIComponent) ?? _expander).BringIntoView();

    protected override string NameCore() => _expander.Header as string ?? TextOf(_expander);
}
