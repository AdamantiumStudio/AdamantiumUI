using System;
using System.Linq;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.Base;

/// <summary>The menu a button drops, as <see cref="Buttons.DropDownButton.DropDownMenu"/> and
/// <see cref="RibbonDropDownButton.DropDownMenu"/> are. It is held as the button's logical child, so it is themed and
/// takes the button's data, as <see cref="InputUIComponent.ContextMenu"/> is. The button owns its close, so the
/// button's own press does not light-dismiss it. When the menu puts itself away, the button hears it. The menu goes
/// with the button: one of its own is discarded with it, one a style shares only given back.</summary>
internal sealed class DroppedMenu(InputUIComponent button, Action putAway)
{
    private bool _ignoredTargetPress;

    public ContextMenu Menu { get; private set; }

    /// <summary>Whether the keyboard is on one of the menu's rows, a submenu's included.</summary>
    public bool HasFocus => Menu != null && FocusManager.Focused is IFundamentalUIComponent focused
                                         && focused.GetSelfAndLogicalAncestors().Contains(Menu);

    /// <summary>The button's menu is another, or none: the old one is put away and given back as it came.</summary>
    public void Take(ContextMenu menu)
    {
        if (Menu is { } old)
        {
            old.PropertyChanged -= OnMenuPropertyChanged;
            old.IsOpen = false;
            old.IgnoreTargetPress = _ignoredTargetPress;
            button.RemoveLogicalChild(old);
        }

        Menu = menu;
        if (menu == null)
        {
            return;
        }

        button.AddLogicalChild(menu);
        // The button's press would light-dismiss the menu first, and the click open it again: the press would look as
        // if it did nothing.
        _ignoredTargetPress = menu.IgnoreTargetPress;
        menu.IgnoreTargetPress = true;
        menu.PropertyChanged += OnMenuPropertyChanged;
    }

    public void Drop() => Menu?.Open(button);

    public void PutAway()
    {
        if (Menu != null)
        {
            Menu.IsOpen = false;
        }
    }

    /// <summary>The button is discarded, its menu set at <paramref name="priority"/>. The menu is not in the visual
    /// tree, so it is let go of here. A menu dropped from this button is put away first, since nothing else would.</summary>
    public void Release(ValuePriority priority)
    {
        if (Menu is not { } menu)
        {
            return;
        }

        // Unhooked whoever it belongs to: a menu a style shares would otherwise keep every button that ever showed it.
        menu.PropertyChanged -= OnMenuPropertyChanged;
        if (menu.IsOpen && ReferenceEquals(menu.PlacementTarget, button))
        {
            menu.IsOpen = false;
        }

        if (!ReferenceEquals(menu.LogicalParent, button))
        {
            return;
        }

        if (button.OwnsMenu(menu, priority))
        {
            DiscardedVisuals.PublishSubtree(menu);
        }
        else
        {
            menu.IgnoreTargetPress = _ignoredTargetPress;
            button.RemoveLogicalChild(menu);
        }
    }

    // A row picked, Escape, a press elsewhere: nothing else tells the button its menu is gone.
    private void OnMenuPropertyChanged(object sender, AdamantiumPropertyChangedEventArgs e)
    {
        if (e.Property == ContextMenu.IsOpenProperty && Equals(e.NewValue, false))
        {
            putAway();
        }
    }
}
