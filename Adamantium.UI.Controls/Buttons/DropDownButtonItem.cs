using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.Buttons;

/// <summary>A row in a <see cref="DropDownButton"/>'s list. Pressed, it closes the list, runs its own
/// <see cref="ButtonBase.Command"/>, then the button's <see cref="DropDownButton.ItemCommand"/> and
/// <see cref="DropDownButton.ItemClick"/>.</summary>
public class DropDownButtonItem : ButtonBase
{
    public static readonly AdamantiumProperty IsHighlightedProperty = AdamantiumProperty.Register(
        nameof(IsHighlighted), typeof(bool), typeof(DropDownButtonItem),
        new PropertyMetadata(false, PropertyMetadataOptions.AffectsRender));

    public static readonly AdamantiumProperty IsAvailableProperty = AdamantiumProperty.RegisterReadOnly(
        nameof(IsAvailable), typeof(bool), typeof(DropDownButtonItem), new PropertyMetadata(true, OnIsAvailableChanged));

    static DropDownButtonItem()
    {
        FocusableProperty.OverrideMetadata(typeof(DropDownButtonItem), new PropertyMetadata(false));
        IsEnabledProperty.OverrideMetadata(typeof(DropDownButtonItem), new PropertyMetadata(true, static (a, e) =>
        {
            var row = (DropDownButtonItem)a;
            Tell(row, (bool)e.OldValue && row.IsAvailable, (bool)e.NewValue && row.IsAvailable);
        }));
    }

    internal DropDownButton Owner { get; set; }

    internal bool IsMade { get; set; }

    internal Core.Resources.Style AddedStyle { get; set; }

    /// <summary>The row the arrows are on while the list is open - where Enter goes.</summary>
    public bool IsHighlighted
    {
        get => GetValue<bool>(IsHighlightedProperty);
        set => SetValue(IsHighlightedProperty, value);
    }

    /// <summary>Whether picking the row would do anything: false while it has no command of its own and the button's
    /// <see cref="DropDownButton.ItemCommand"/> cannot run for its item. The theme draws it as disabled.</summary>
    public bool IsAvailable
    {
        get => GetValue<bool>(IsAvailableProperty);
        internal set => SetValue(IsAvailableProperty, value);
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        OwnerOf()?.HighlightFromContainer(this);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!IsAvailable)
        {
            e.Handled = true;
            return;
        }

        base.OnPointerPressed(e);
    }

    protected override void OnClick()
    {
        if (!IsAvailable) return;

        var owner = OwnerOf();
        object item = this;
        if (owner != null && !owner.Take(this, out item)) return;

        base.OnClick();
        owner?.Picked(this, item);
    }

    protected override void OnPropertyChanged(AdamantiumPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == CommandProperty || e.Property == IsEnabledProperty)
        {
            OwnerOf()?.Refresh(this);
        }
    }

    private static void OnIsAvailableChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        var row = (DropDownButtonItem)a;
        Tell(row, (bool)e.OldValue && row.IsEnabled, (bool)e.NewValue && row.IsEnabled);
    }

    private static void Tell(DropDownButtonItem row, bool was, bool now)
    {
        if (was != now && Core.Automation.AutomationEvents.IsListening)
        {
            Core.Automation.AutomationEvents.RaisePropertyChanged(row, Core.Automation.AutomationProperty.IsEnabled, was, now);
        }
    }

    protected override Core.Automation.AutomationPeer OnCreateAutomationPeer() =>
        new Automation.DropDownButtonItemAutomationPeer(this);

    private DropDownButton OwnerOf()
    {
        if (IsMade) return Owner;

        if (Owner != null && Owner.Items.Contains(this)) return Owner;

        return ItemsControl.AuthoredOwner(this) is DropDownButton authored && authored.Items.Contains(this) ? authored : null;
    }
}
