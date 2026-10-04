using System.Collections.Generic;
using System.Linq;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="TabItem"/>: called by its header, selected the way a click selects it.</summary>
public class TabItemAutomationPeer : ContentControlAutomationPeer, ISelectionItemProvider
{
    public TabItemAutomationPeer(TabItem owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.TabItem;

    public bool IsSelected => ((TabItem)Owner).IsSelected;

    public AutomationPeer SelectionContainer => OwningTabControl()?.GetAutomationPeer();

    public void Select() => OwningTabControl()?.SelectTab((TabItem)Owner);

    protected override string NameCore() => ((TabItem)Owner).Header as string ?? TextOf(Owner);

    protected override IReadOnlyList<AutomationPeer> ChildrenCore() => [];

    private TabControl OwningTabControl() => Owner.GetVisualAncestors().OfType<TabControl>().FirstOrDefault();
}
