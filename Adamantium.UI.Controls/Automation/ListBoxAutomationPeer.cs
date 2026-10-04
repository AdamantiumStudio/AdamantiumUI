using System.Collections.Generic;
using System.Linq;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="ListBox"/>: a list of its items, selected one or many at a time.</summary>
public class ListBoxAutomationPeer : ItemsControlAutomationPeer, ISelectionProvider
{
    public ListBoxAutomationPeer(ListBox owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.List;

    public bool CanSelectMultiple => ((ListBox)Owner).SelectionMode != SelectionMode.Single;

    public IReadOnlyList<AutomationPeer> GetSelection() =>
        [.. GetChildren().Where(child => child.GetPattern(PatternId.SelectionItem) is ISelectionItemProvider { IsSelected: true })];
}
