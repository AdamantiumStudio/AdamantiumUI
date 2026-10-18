using System.Collections.Generic;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="SegmentedControlItem"/>: one choice, picked the way a press picks it.</summary>
public class SegmentedControlItemAutomationPeer : ContentControlAutomationPeer, ISelectionItemProvider
{
    private readonly SegmentedControlItem _segment;

    public SegmentedControlItemAutomationPeer(SegmentedControlItem owner) : base(owner)
    {
        _segment = owner;
    }

    public override AutomationControlType ControlType => AutomationControlType.ListItem;

    public bool IsSelected => _segment.IsSelected;

    public AutomationPeer SelectionContainer => ItemsOwnerPeer();

    public void Select() => _segment.Owner?.PickFromContainer(_segment);

    protected override string NameCore() => base.NameCore() ?? TextOf(Owner);

    protected override IReadOnlyList<AutomationPeer> ChildrenCore() => [];
}
