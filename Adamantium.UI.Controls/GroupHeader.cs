using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls;

/// <summary>A group's header in an <see cref="ItemsControl"/>: a row of the panel before the group's items, showing the
/// group through its <see cref="GroupStyle.HeaderTemplate"/>. Not an item - it is not selected, the arrows pass it by -
/// and automation reads it as a group named after its group. Where <see cref="CanCollapse"/>, a click on it folds the
/// group to the header and opens it again; the theme draws the header and its arrow. A drag resting on a folded header
/// opens it, so what is dragged can go in among its items.</summary>
public class GroupHeader : ContentControl, ISpringLoadable
{
    public static readonly AdamantiumProperty IsExpandedProperty = AdamantiumProperty.Register(nameof(IsExpanded),
        typeof(bool), typeof(GroupHeader), new PropertyMetadata(true, OnIsExpandedChanged));

    public static readonly AdamantiumProperty CanCollapseProperty = AdamantiumProperty.Register(nameof(CanCollapse),
        typeof(bool), typeof(GroupHeader), new PropertyMetadata(false));

    private ItemsControl _owner;
    private int _place = -1;
    private bool _showing;

    public GroupHeader()
    {
        Focusable = false;
    }

    /// <summary>Whether the group's items are shown under the header; false folds the group to it. The list keeps it
    /// for the group, by its name, across changes of the items.</summary>
    public bool IsExpanded
    {
        get => GetValue<bool>(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    /// <summary>Whether a click on the header folds its group: <see cref="GroupStyle.CanCollapse"/> of its level.</summary>
    public bool CanCollapse
    {
        get => GetValue<bool>(CanCollapseProperty);
        set => SetValue(CanCollapseProperty, value);
    }

    /// <summary>Folds the group or opens it, where it <see cref="CanCollapse"/>. True when it did.</summary>
    public bool Toggle()
    {
        if (!CanCollapse)
        {
            return false;
        }

        IsExpanded = !IsExpanded;
        return true;
    }

    /// <summary>Opens a folded group the drag rests on.</summary>
    public void SpringLoad()
    {
        if (!IsExpanded)
        {
            Toggle();
        }
    }

    /// <summary>Shows the group at <paramref name="place"/> of <paramref name="owner"/>: its expansion is the place's, and
    /// not told back.</summary>
    internal void ShowPlace(ItemsControl owner, int place, bool isExpanded, bool canCollapse)
    {
        _owner = owner;
        _place = place;
        CanCollapse = canCollapse;
        _showing = true;
        try
        {
            IsExpanded = isExpanded;
        }
        finally
        {
            _showing = false;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.Handled && IsEnabled && e.ChangedButton == MouseButtons.Left && Toggle())
        {
            e.Handled = true;
        }
    }

    private static void OnIsExpandedChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        var header = (GroupHeader)component;
        if (!header._showing && header._owner != null && header._place >= 0)
        {
            header._owner.SetGroupExpanded(header._place, (bool)e.NewValue);
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new GroupHeaderAutomationPeer(this);
}
