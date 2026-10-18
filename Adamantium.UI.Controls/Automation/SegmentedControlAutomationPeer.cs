using System.Collections.Generic;
using System.Linq;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="SegmentedControl"/>: a list of choices, one of them chosen.</summary>
public class SegmentedControlAutomationPeer : ItemsControlAutomationPeer, ISelectionProvider
{
    public SegmentedControlAutomationPeer(SegmentedControl owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.List;

    public bool CanSelectMultiple => false;

    public bool IsSelectionRequired => false;

    public IReadOnlyList<AutomationPeer> GetSelection() =>
        [.. GetChildren().Where(child => child.GetPattern(PatternId.SelectionItem) is ISelectionItemProvider { IsSelected: true })];
}
