using Adamantium.Core.Commands;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.Buttons;

/// <summary>A button of two halves: its body runs <see cref="Command"/> and raises <see cref="Click"/>, its arrow
/// (the template's <c>PART_DropDownArea</c>) drops the list of its items. Enter and Space press the body; Alt+Down and
/// F4 open the list.</summary>
public class SplitButton : DropDownButton
{
    public static readonly AdamantiumProperty CommandProperty = AdamantiumProperty.Register(nameof(Command),
        typeof(ICommand), typeof(SplitButton), new PropertyMetadata(null, OnCommandChanged));

    public static readonly AdamantiumProperty CommandParameterProperty = AdamantiumProperty.Register(
        nameof(CommandParameter), typeof(object), typeof(SplitButton), new PropertyMetadata(null, OnCommandParameterChanged));

    public static readonly AdamantiumProperty CommandTargetProperty = AdamantiumProperty.Register(nameof(CommandTarget),
        typeof(IInputComponent), typeof(SplitButton), new PropertyMetadata(null, OnCommandParameterChanged));

    public static readonly AdamantiumProperty IsPressedProperty = AdamantiumProperty.RegisterReadOnly(nameof(IsPressed),
        typeof(bool), typeof(SplitButton), new PropertyMetadata(false));

    public static readonly AdamantiumProperty IsBodyEnabledProperty = AdamantiumProperty.RegisterReadOnly(
        nameof(IsBodyEnabled), typeof(bool), typeof(SplitButton), new PropertyMetadata(true));

    public static readonly AdamantiumProperty IsPointerOverDropDownProperty = AdamantiumProperty.RegisterReadOnly(
        nameof(IsPointerOverDropDown), typeof(bool), typeof(SplitButton), new PropertyMetadata(false));

    public static readonly RoutedEvent ClickEvent = EventManager.RegisterRoutedEvent(nameof(Click),
        RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(SplitButton));

    private CommandSourceListener<SplitButton> _commandListener;
    private InputUIComponent _dropDownArea;

    /// <summary>What the body runs.</summary>
    public ICommand Command
    {
        get => GetValue<ICommand>(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    /// <summary>Where a <see cref="RoutedCommand"/> of the body starts; null starts it from the button.</summary>
    public IInputComponent CommandTarget
    {
        get => GetValue<IInputComponent>(CommandTargetProperty);
        set => SetValue(CommandTargetProperty, value);
    }

    /// <summary>Whether the body is held down.</summary>
    public bool IsPressed
    {
        get => GetValue<bool>(IsPressedProperty);
        private set => SetValue(IsPressedProperty, value);
    }

    /// <summary>Whether the body can be pressed - false while <see cref="Command"/> cannot run. The arrow and the list
    /// stay reachable.</summary>
    public bool IsBodyEnabled
    {
        get => GetValue<bool>(IsBodyEnabledProperty);
        private set => SetValue(IsBodyEnabledProperty, value);
    }

    /// <summary>Whether the pointer is over the arrow - the theme lights the half a click would press.</summary>
    public bool IsPointerOverDropDown
    {
        get => GetValue<bool>(IsPointerOverDropDownProperty);
        private set => SetValue(IsPointerOverDropDownProperty, value);
    }

    /// <summary>Raised when the body is clicked, after <see cref="Command"/> ran.</summary>
    public event RoutedEventHandler Click
    {
        add => AddHandler(ClickEvent, value);
        remove => RemoveHandler(ClickEvent, value);
    }

    /// <summary>Clicks the body, if it is enabled.</summary>
    public void PerformClick()
    {
        if (IsEnabled && IsBodyEnabled) OnClick();
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _dropDownArea = GetTemplateChild("PART_DropDownArea") as InputUIComponent;
    }

    public override void OnRemoveTemplate()
    {
        base.OnRemoveTemplate();
        _dropDownArea = null;
    }

    /// <summary>The body was clicked: runs <see cref="Command"/> and raises <see cref="Click"/>.</summary>
    protected virtual void OnClick()
    {
        CommandManager.ExecuteFrom(Command, CommandParameter, this, CommandTarget);
        RaiseEvent(new RoutedEventArgs(ClickEvent, this) { RoutedEvent = ClickEvent });
        if (AutomationEvents.IsListening)
        {
            AutomationEvents.Raise(this, AutomationEvent.Invoked);
        }
    }

    protected override void OnHeaderPressed(PointerPressedEventArgs e)
    {
        if (OverDropDown(e))
        {
            base.OnHeaderPressed(e);
            return;
        }

        Open(false);
        if (!IsBodyEnabled) return;

        e.Pointer.Capture(this);
        IsPressed = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.ChangedButton != MouseButtons.Left || !ReferenceEquals(e.Pointer.Captured, this))
        {
            return;
        }

        e.Handled = true;
        var wasPressed = IsPressed;
        IsPressed = false;
        e.Pointer.Capture(null);
        IsPointerOverDropDown = OverDropDown(e);
        if (wasPressed && IsEnabled && IsBodyEnabled && !IsDropDownOpen && OverBody(e))
        {
            OnClick();
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (ReferenceEquals(e.Pointer.Captured, this))
        {
            IsPressed = IsBodyEnabled && !IsDropDownOpen && e.Properties.IsLeftButtonPressed && OverBody(e);
            return;
        }

        IsPointerOverDropDown = OverDropDown(e);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        IsPointerOverDropDown = false;
        if (ReferenceEquals(e.Pointer.Captured, this)) IsPressed = false;
    }

    protected override void OnDropDownOpenChanged(bool open)
    {
        base.OnDropDownOpenChanged(open);
        if (open) IsPressed = false;
    }

    protected override void OnPointerCaptureLost(PointerEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        IsPressed = false;
    }

    protected override bool OpensOn(Key key) => false;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || !IsEnabled || !IsBodyEnabled || IsDropDownOpen || e.IsRepeated)
        {
            return;
        }

        if (e.Key == Key.Space)
        {
            IsPressed = true;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            OnClick();
            e.Handled = true;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key != Key.Space || !IsPressed)
        {
            return;
        }

        IsPressed = false;
        e.Handled = true;
        if (IsEnabled && IsBodyEnabled) OnClick();
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        IsPressed = false;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _commandListener?.Attached();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _commandListener?.Detached();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new SplitButtonAutomationPeer(this);

    private static void OnCommandChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        var button = (SplitButton)a;
        button._commandListener ??= new CommandSourceListener<SplitButton>(button, static b => b.UpdateCanExecute());
        button._commandListener.Listen(e.NewValue as ICommand);
        button.UpdateCanExecute();
    }

    private static void OnCommandParameterChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e) =>
        ((SplitButton)a).UpdateCanExecute();

    private void UpdateCanExecute()
    {
        var state = CommandManager.QueryFrom(Command, CommandParameter, this, CommandTarget);
        IsBodyEnabled = state.CanExecute;
        if (!state.CanExecute) IsPressed = false;
        OnCommandState(state);
    }

    /// <summary>Takes what the element a routed command reaches says of it beyond whether it can run.</summary>
    protected virtual void OnCommandState(CommandState state)
    {
    }

    private bool OverBody(PointerEventArgs e)
    {
        var at = e.GetPosition(this);
        return at.X >= 0 && at.Y >= 0 && at.X <= RenderSize.Width && at.Y <= RenderSize.Height && !OverDropDown(e);
    }

    private bool OverDropDown(PointerEventArgs e)
    {
        if (_dropDownArea == null) return false;

        var point = e.GetPosition(_dropDownArea);
        return point.X >= 0 && point.X < _dropDownArea.ActualWidth && point.Y >= 0 && point.Y < _dropDownArea.ActualHeight;
    }
}
