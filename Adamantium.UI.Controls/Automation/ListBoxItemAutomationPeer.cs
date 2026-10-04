using System.Linq;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="ListBoxItem"/> or a <see cref="DropDownItem"/>: an item of its list, selected the
/// way a click selects it. What its template shows - a button on a card, say - stays below it.</summary>
public class ListBoxItemAutomationPeer : ContentControlAutomationPeer, ISelectionItemProvider, IScrollItemProvider
{
    public ListBoxItemAutomationPeer(ListBoxItem owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.ListItem;

    public bool IsSelected => ((ListBoxItem)Owner).IsSelected;

    public AutomationPeer SelectionContainer => GetParent();

    public override AutomationPeer GetParent() => ItemsOwnerPeer() ?? base.GetParent();

    public void Select()
    {
        switch (Owner)
        {
            case DropDownItem item:
                (item.Owner ?? item.GetLogicalAncestors().OfType<DropDown>().FirstOrDefault())?.SelectFromContainer(item);
                break;
            case ListBoxItem item:
                item.GetLogicalAncestors().OfType<ListBox>().FirstOrDefault()?.SelectFromContainer(item);
                break;
        }
    }

    public void ScrollIntoView() => Owner.BringIntoView();

    protected override string NameCore() => base.NameCore() ?? TextOf(Owner);
}
