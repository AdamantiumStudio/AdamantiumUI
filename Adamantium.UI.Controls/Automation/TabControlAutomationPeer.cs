using System.Collections.Generic;
using System.Linq;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="TabControl"/>: its tabs, and the page of the one selected.</summary>
public class TabControlAutomationPeer : UIComponentAutomationPeer, ISelectionProvider
{
    public TabControlAutomationPeer(TabControl owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.Tab;

    public bool CanSelectMultiple => false;

    public IReadOnlyList<AutomationPeer> GetSelection() =>
        GetChildren().Where(peer => peer is TabItemAutomationPeer { IsSelected: true }).ToList();
}
