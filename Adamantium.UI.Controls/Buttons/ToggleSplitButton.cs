using System;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.Buttons;

/// <summary>A <see cref="SplitButton"/> whose body stays down: a click on it turns <see cref="IsChecked"/> over before
/// its command runs - a list marker that is on or off, with its kinds in the list.</summary>
public class ToggleSplitButton : SplitButton
{
    public static readonly AdamantiumProperty IsCheckedProperty = AdamantiumProperty.Register(nameof(IsChecked),
        typeof(bool), typeof(ToggleSplitButton),
        new PropertyMetadata(false, PropertyMetadataOptions.BindsTwoWayByDefault, OnIsCheckedChanged));

    /// <summary>Whether the body is down. Two-way by default.</summary>
    public bool IsChecked
    {
        get => GetValue<bool>(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    /// <summary>Raised when <see cref="IsChecked"/> turns over.</summary>
    public event EventHandler IsCheckedChanged;

    protected override void OnClick()
    {
        SetCurrentValue(IsCheckedProperty, !IsChecked);
        base.OnClick();
    }

    /// <summary>Takes the state the command's target answers with - a list marker on where the caret is.</summary>
    protected override void OnCommandState(Core.Input.CommandState state)
    {
        if (state.IsChecked is { } on) SetCurrentValue(IsCheckedProperty, on);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ToggleSplitButtonAutomationPeer(this);

    private static void OnIsCheckedChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        var button = (ToggleSplitButton)a;
        button.IsCheckedChanged?.Invoke(button, EventArgs.Empty);
        if (AutomationEvents.IsListening)
        {
            AutomationEvents.RaisePropertyChanged(button, AutomationProperty.ToggleState,
                ToggleButtonAutomationPeer.StateOf((bool)e.OldValue), ToggleButtonAutomationPeer.StateOf((bool)e.NewValue));
        }
    }
}
