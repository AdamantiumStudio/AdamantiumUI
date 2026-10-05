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

    /// <summary>What its header holds to act on - its close and pin buttons, when its tab control offers them; the text it
    /// shows is its name.</summary>
    protected override IReadOnlyList<AutomationPeer> ChildrenCore()
    {
        var tab = (TabItem)Owner;
        var close = tab.ShowCloseButton ? null : tab.GetTemplateChild("PART_CloseButton");
        var pin = tab.ShowPinButton ? null : tab.GetTemplateChild("PART_PinButton");
        var children = new List<AutomationPeer>();
        Collect(Owner, children);
        return [.. children.Where(child => child.ControlType != AutomationControlType.Text && !Withheld(child))];

        bool Withheld(AutomationPeer child) => child is UIComponentAutomationPeer peer
                                               && (ReferenceEquals(peer.Owner, close) || ReferenceEquals(peer.Owner, pin));
    }

    private TabControl OwningTabControl() => Owner.GetVisualAncestors().OfType<TabControl>().FirstOrDefault();
}
