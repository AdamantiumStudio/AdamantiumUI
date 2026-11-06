using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls;

/// <summary>A command that opens a <see cref="ContextMenu"/> flyout; <see cref="IsDropDownOpen"/> is the open state, and
/// <see cref="Primitives.ToggleButton.IsChecked"/> stays what its command says of itself.</summary>
public class RibbonDropDownButton : RibbonToggleButton, IKeyTipTarget
{
    public static readonly AdamantiumProperty IsDropDownOpenProperty = AdamantiumProperty.Register(
        nameof(IsDropDownOpen), typeof(bool), typeof(RibbonDropDownButton),
        new PropertyMetadata(false, PropertyMetadataOptions.BindsTwoWayByDefault, OnIsDropDownOpenChanged));

    /// <summary>Whether its menu is open. Binds two-way by default. It stays false with no menu to drop or while the
    /// command cannot be used.</summary>
    public bool IsDropDownOpen
    {
        get => GetValue<bool>(IsDropDownOpenProperty);
        set => SetValue(IsDropDownOpenProperty, value);
    }

    /// <summary>Reached by its key tip: drop the menu AND step the keyboard into it. A command opened from the keyboard
    /// has to stay reachable from it - the rows are drawn in the overlay, so a plain click-equivalent leaves the
    /// keyboard standing on the button with a list open in front of it and no way in.</summary>
    public void PressKeyTip()
    {
        _pressedByPointer = false;
        SetCurrentValue(IsDropDownOpenProperty, true);
        if (IsDropDownOpen)
        {
            DropDownMenu?.MoveKeyboardInsideWhenReady();
        }
    }

    public static readonly AdamantiumProperty DropDownMenuProperty = AdamantiumProperty.Register(nameof(DropDownMenu),
        typeof(ContextMenu), typeof(RibbonDropDownButton), new PropertyMetadata(null, OnDropDownMenuChanged));

    /// <summary>The menu this command drops. Held as a logical child, so it is themed and inherits DataContext - the
    /// same arrangement <see cref="Base.InputUIComponent.ContextMenu"/> uses. A menu given while the button is open is
    /// opened in the place of the one it replaces.</summary>
    public ContextMenu DropDownMenu
    {
        get => GetValue<ContextMenu>(DropDownMenuProperty);
        set => SetValue(DropDownMenuProperty, value);
    }

    private static void OnDropDownMenuChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is not RibbonDropDownButton button) return;

        if (e.OldValue is ContextMenu old)
        {
            old.PropertyChanged -= button.OnMenuPropertyChanged;
            old.IsOpen = false;
            button.RemoveLogicalChild(old);
        }

        if (e.NewValue is ContextMenu menu)
        {
            button.AddLogicalChild(menu);
            // The BUTTON owns the close (IsDropDownOpen drives the flyout), so its own press must not light-dismiss first -
            // the dismiss would close, the click would re-open, and the second press would look like it did nothing.
            menu.IgnoreTargetPress = true;
            menu.PropertyChanged += button.OnMenuPropertyChanged;
            if (button.IsDropDownOpen)
            {
                menu.Open(button);
            }
        }
        else if (button.IsDropDownOpen)
        {
            button.SetCurrentValue(IsDropDownOpenProperty, false);
        }
    }

    // The menu closed itself (an outside press, Escape, or a row picked). Nothing else hears that, so the command would
    // stay lit over a flyout that is gone.
    private void OnMenuPropertyChanged(object sender, AdamantiumPropertyChangedEventArgs e)
    {
        if (e.Property != ContextMenu.IsOpenProperty) return;
        if (!Equals(e.NewValue, false)) return;

        // Dropped by a click and put away with nothing picked - Escape, a press elsewhere - the keyboard goes back to the
        // field the ribbon acts for, as after a command; dropped from the keyboard, it stays on the button.
        var returnFocus = _droppedByPointer && ReferenceEquals(FocusManager.Focused, this);
        _droppedByPointer = false;
        SetCurrentValue(IsDropDownOpenProperty, false);
        if (returnFocus)
        {
            FocusManager.FocusToReturnTo(this)?.Focus();
        }
    }

    private bool _pressedByPointer;
    private bool _droppedByPointer;
    private bool _toggling;
    private bool _dropped;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        _pressedByPointer = e.ChangedButton == MouseButtons.Left;
        base.OnPointerPressed(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        _pressedByPointer = false;
        base.OnKeyDown(e);
    }

    private protected virtual bool ShowsCommandChecked => false;

    protected override void OnCommandState(CommandState state)
    {
        if (ShowsCommandChecked)
        {
            base.OnCommandState(state);
        }
    }

    /// <summary>A click drops or puts away the menu; what is checked is left to the command.</summary>
    protected override void OnToggle()
    {
        _toggling = true;
        try
        {
            SetCurrentValue(IsDropDownOpenProperty, !IsDropDownOpen);
        }
        finally
        {
            _toggling = false;
        }
    }

    private static void OnIsDropDownOpenChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is not RibbonDropDownButton button) return;

        var menu = button.DropDownMenu;
        if ((bool)e.NewValue)
        {
            if (menu == null || !button.IsEnabled)
            {
                button.SetCurrentValue(IsDropDownOpenProperty, false);
                return;
            }

            button._dropped = true;
            button._droppedByPointer = button._pressedByPointer;
            button._pressedByPointer = false;
            AutomationEvents.RaiseExpandCollapse(button, true);
            menu.Open(button);
            return;
        }

        if (!button._dropped)
        {
            return;
        }

        button._dropped = false;
        if (!button._toggling)
        {
            button._droppedByPointer = false;
        }

        AutomationEvents.RaiseExpandCollapse(button, false);
        if (menu != null) menu.IsOpen = false;
    }

    /// <summary>The menu is not in the visual tree, so it goes with the button the way its context menu does: one of its
    /// own is discarded, one a style shares is only unhooked.</summary>
    protected override void OnDiscarded()
    {
        base.OnDiscarded();

        if (DropDownMenu is not { } menu) return;

        // Unhooked whoever it belongs to: a menu a style shares would otherwise keep every button that ever showed it.
        menu.PropertyChanged -= OnMenuPropertyChanged;
        if (!ReferenceEquals(menu.LogicalParent, this)) return;

        if (OwnsMenu(menu, GetBaseValuePriority(DropDownMenuProperty)))
        {
            DiscardedVisuals.PublishSubtree(menu);
        }
        else
        {
            RemoveLogicalChild(menu);
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new RibbonDropDownButtonAutomationPeer(this);
}
