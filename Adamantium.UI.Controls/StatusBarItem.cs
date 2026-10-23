using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls;

/// <summary>A field of a <see cref="StatusBar"/>: a word, a number or a picture that tells one thing. Given a
/// <see cref="ButtonBase.Command"/> or a <see cref="Base.InputUIComponent.ContextMenu"/> it acts on a press, as an IDE's
/// fields do - runs the command, or opens the menu where its author placed it and closes it on the next press - and
/// shows it under the pointer; without them it only tells, and a press goes by it.</summary>
public class StatusBarItem : ButtonBase
{
    public static readonly AdamantiumProperty IsInteractiveProperty = AdamantiumProperty.RegisterReadOnly(
        nameof(IsInteractive), typeof(bool), typeof(StatusBarItem), new PropertyMetadata(false));

    private bool _menuIgnoredTargetPress;
    private PlacementMode _menuPlacement;

    static StatusBarItem()
    {
        FocusableProperty.OverrideMetadata(typeof(StatusBarItem), new PropertyMetadata(false));
    }

    /// <summary>Whether a press does something: the field has a command or a menu.</summary>
    public bool IsInteractive
    {
        get => GetValue<bool>(IsInteractiveProperty);
        private set => SetValue(IsInteractiveProperty, value);
    }

    protected override bool TakesPresses => IsInteractive;

    protected override void OnPropertyChanged(AdamantiumPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property != CommandProperty && e.Property != ContextMenuProperty)
        {
            return;
        }

        IsInteractive = Command != null || ContextMenu != null;
        if (e.Property != ContextMenuProperty)
        {
            return;
        }

        if (e.OldValue is ContextMenu old)
        {
            old.IgnoreTargetPress = _menuIgnoredTargetPress;
        }

        if (e.NewValue is ContextMenu menu)
        {
            _menuIgnoredTargetPress = menu.IgnoreTargetPress;
            _menuPlacement = menu.Placement;
            menu.IgnoreTargetPress = true;
        }
    }

    protected override void OnClick()
    {
        if (!IsInteractive)
        {
            return;
        }

        if (ContextMenu is { IsOpen: true } open)
        {
            open.IsOpen = false;
            if (Command == null)
            {
                return;
            }
        }

        base.OnClick();
        if (Command != null || ContextMenu is not { } menu || menu.IsOpen)
        {
            return;
        }

        if (menu.Placement == PlacementMode.Relative)
        {
            menu.Placement = _menuPlacement;
            menu.HorizontalOffset = 0;
            menu.VerticalOffset = 0;
        }

        menu.Open(this);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new StatusBarItemAutomationPeer(this);
}
