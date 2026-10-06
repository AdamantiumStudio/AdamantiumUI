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

    /// <summary>Makes it the one selected item - in a list that selects many as well, where a click would toggle it.
    /// </summary>
    public void Select()
    {
        switch (Owner)
        {
            case DropDownItem item:
                (item.Owner ?? item.GetLogicalAncestors().OfType<DropDown>().FirstOrDefault())?.SelectFromContainer(item);
                break;
            case ListBoxItem item when List() is { } list:
                list.SelectOnlyItem(list.ItemOf(item));
                break;
        }
    }

    public void AddToSelection()
    {
        if (Owner is DropDownItem || List() is not { } list)
        {
            ISelectionItemProvider.AddToOnlyOne(this);
            return;
        }

        list.AddItemToSelection(list.ItemOf((ListBoxItem)Owner));
    }

    public void RemoveFromSelection()
    {
        if (Owner is DropDownItem || List() is not { } list)
        {
            ISelectionItemProvider.RemoveFromOnlyOne(this);
            return;
        }

        list.RemoveItemFromSelection(list.ItemOf((ListBoxItem)Owner));
    }

    private ListBox List() => Owner.GetLogicalAncestors().OfType<ListBox>().FirstOrDefault();

    public void ScrollIntoView() => Owner.BringIntoView();

    /// <summary>The text it shows, else what its list calls the item it holds - as when the list has not made it.</summary>
    protected override string NameCore() => base.NameCore() ?? TextOf(Owner) ??
        (ItemsOwnerPeer()?.NameOfItem(((ContentControl)Owner).Content) is { Length: > 0 } name ? name : null);
}
