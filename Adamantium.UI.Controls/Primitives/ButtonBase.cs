using Adamantium.ProceduralGeometry;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.Core.Commands;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.Primitives;

/// <summary>
/// Base class for controls that respond to a click - a mouse press/release over the control, or the Space/Enter keys
/// while focused - and optionally invoke an <see cref="ICommand"/>. Carries the chrome (border/corner/padding) that
/// button templates bind to.
/// </summary>
public abstract class ButtonBase : ContentControl
{
    // State brushes the default template's triggers project onto the chrome for hover/press/disabled. Exposing them as
    // properties is what lets ONE template serve every button variant: the Accent style just overrides these brushes
    // (via {ThemeResource}), no second template. Set by the theme; null means "no change in that state".
    public static readonly AdamantiumProperty BackgroundPointerOverProperty = AdamantiumProperty.Register(
        nameof(BackgroundPointerOver), typeof(Brush), typeof(ButtonBase), new PropertyMetadata(default(Brush)));

    public static readonly AdamantiumProperty BackgroundPressedProperty = AdamantiumProperty.Register(
        nameof(BackgroundPressed), typeof(Brush), typeof(ButtonBase), new PropertyMetadata(default(Brush)));

    public static readonly AdamantiumProperty BackgroundDisabledProperty = AdamantiumProperty.Register(
        nameof(BackgroundDisabled), typeof(Brush), typeof(ButtonBase), new PropertyMetadata(default(Brush)));

    public static readonly AdamantiumProperty ForegroundPressedProperty = AdamantiumProperty.Register(
        nameof(ForegroundPressed), typeof(Brush), typeof(ButtonBase), new PropertyMetadata(default(Brush)));

    public static readonly AdamantiumProperty ForegroundDisabledProperty = AdamantiumProperty.Register(
        nameof(ForegroundDisabled), typeof(Brush), typeof(ButtonBase), new PropertyMetadata(default(Brush)));

    public static readonly AdamantiumProperty ClickModeProperty = AdamantiumProperty.Register(nameof(ClickMode),
        typeof(ClickMode), typeof(ButtonBase), new PropertyMetadata(ClickMode.Release));

    public static readonly AdamantiumProperty IsPressedProperty = AdamantiumProperty.RegisterReadOnly(nameof(IsPressed),
        typeof(bool), typeof(ButtonBase), new PropertyMetadata(false));

    public static readonly AdamantiumProperty CommandProperty = AdamantiumProperty.Register(nameof(Command),
        typeof(ICommand), typeof(ButtonBase), new PropertyMetadata(null, OnCommandChanged));

    public static readonly AdamantiumProperty CommandParameterProperty = AdamantiumProperty.Register(nameof(CommandParameter),
        typeof(object), typeof(ButtonBase), new PropertyMetadata(null, OnCommandParameterChanged));

    public static readonly AdamantiumProperty CommandTargetProperty = AdamantiumProperty.Register(nameof(CommandTarget),
        typeof(IInputComponent), typeof(ButtonBase), new PropertyMetadata(null, OnCommandParameterChanged));

    public static readonly RoutedEvent ClickEvent = EventManager.RegisterRoutedEvent(nameof(Click),
        RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ButtonBase));

    static ButtonBase()
    {
        // A button IS a keyboard-focus target (space/enter activate it) - opt in, since the base default is now false.
        // Metadata priority (not a ctor set), so a {Binding}/Style/Trigger can still override it (e.g. a scrollbar's
        // repeat buttons set Focusable="False" in their template). Covers Button/RepeatButton/Toggle*/CheckBox/Radio.
        FocusableProperty.OverrideMetadata(typeof(ButtonBase), new PropertyMetadata(true));
    }

    protected ButtonBase()
    {
        AddHandler(Gestures.HoldingEvent, new HoldingEventHandler(OnHolding));
    }

    /// <summary>Whether a finger or a pen held on the button is a long press rather than a press: the button lets go of
    /// its pressed look and is not clicked by the lift. A repeat button says no - holding it is how it repeats.</summary>
    protected virtual bool LongPressCancelsClick => true;

    private void OnHolding(object sender, HoldingEventArgs e)
    {
        if (e.HoldingState == HoldingState.Started && LongPressCancelsClick) IsPressed = false;
    }

    public event RoutedEventHandler Click
    {
        add => AddHandler(ClickEvent, value);
        remove => RemoveHandler(ClickEvent, value);
    }

    public Brush BackgroundPointerOver
    {
        get => GetValue<Brush>(BackgroundPointerOverProperty);
        set => SetValue(BackgroundPointerOverProperty, value);
    }

    public Brush BackgroundPressed
    {
        get => GetValue<Brush>(BackgroundPressedProperty);
        set => SetValue(BackgroundPressedProperty, value);
    }

    public Brush BackgroundDisabled
    {
        get => GetValue<Brush>(BackgroundDisabledProperty);
        set => SetValue(BackgroundDisabledProperty, value);
    }

    public Brush ForegroundPressed
    {
        get => GetValue<Brush>(ForegroundPressedProperty);
        set => SetValue(ForegroundPressedProperty, value);
    }

    public Brush ForegroundDisabled
    {
        get => GetValue<Brush>(ForegroundDisabledProperty);
        set => SetValue(ForegroundDisabledProperty, value);
    }

    public ClickMode ClickMode
    {
        get => GetValue<ClickMode>(ClickModeProperty);
        set => SetValue(ClickModeProperty, value);
    }

    public bool IsPressed
    {
        get => GetValue<bool>(IsPressedProperty);
        private set => SetValue(IsPressedProperty, value);
    }

    public ICommand Command
    {
        get => GetValue<ICommand>(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object CommandParameter
    {
        get => GetValue<object>(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    /// <summary>Where a <see cref="RoutedCommand"/> starts; null starts it from the button, and from a ribbon or a menu it
    /// goes on to the field with the focus.</summary>
    public IInputComponent CommandTarget
    {
        get => GetValue<IInputComponent>(CommandTargetProperty);
        set => SetValue(CommandTargetProperty, value);
    }

    // Track the command's CanExecuteChanged so the button auto-enables/disables.
    private CommandSourceListener<ButtonBase> _commandListener;

    private static void OnCommandChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is ButtonBase button) button.OnCommandChanged(e.NewValue as ICommand);
    }

    private static void OnCommandParameterChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is ButtonBase button) button.UpdateCanExecute();
    }

    private void OnCommandChanged(ICommand newCommand)
    {
        _commandListener ??= new CommandSourceListener<ButtonBase>(this, static b => b.UpdateCanExecute());
        _commandListener.Listen(newCommand);
        UpdateCanExecute();
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

    private void UpdateCanExecute()
    {
        var state = CommandManager.QueryFrom(Command, CommandParameter, this, CommandTarget);
        IsEnabled = state.CanExecute;
        OnCommandState(state);
    }

    /// <summary>Takes what the element a routed command reaches says of it beyond whether it can run - a toggle shows
    /// whether it is on.</summary>
    protected virtual void OnCommandState(CommandState state)
    {
    }

    /// <summary>Programmatically clicks the button (e.g. a default/cancel button activated by Enter/Esc), if enabled.</summary>
    public void PerformClick()
    {
        if (IsEnabled) OnClick();
    }

    protected virtual void OnClick()
    {
        CommandManager.ExecuteFrom(Command, CommandParameter, this, CommandTarget);
        RaiseEvent(new RoutedEventArgs(ClickEvent, this) { RoutedEvent = ClickEvent });
        if (AutomationEvents.IsListening && GetAutomationPeer() is IInvokeProvider)
        {
            AutomationEvents.Raise(this, AutomationEvent.Invoked);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEnabled || e.ChangedButton != MouseButtons.Left || e.Handled)
        {
            return;
        }

        e.Handled = true;

        // Capture so move/up keep arriving even when the pointer leaves the button (press-drag-release semantics).
        Focus();
        e.Pointer.Capture(this);
        IsPressed = true;

        if (ClickMode == ClickMode.Press)
        {
            OnClick();
        }
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

        var position = e.GetPosition(this);
        var over = position.X >= 0 && position.Y >= 0
                   && position.X <= RenderSize.Width && position.Y <= RenderSize.Height;

        var longPress = e.Pointer.IsHolding && LongPressCancelsClick;
        if (wasPressed && IsEnabled && over && ClickMode == ClickMode.Release && !longPress)
        {
            OnClick();
        }
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);

        if (ClickMode == ClickMode.Hover)
        {
            if (IsEnabled)
            {
                OnClick();
            }

            return;
        }

        if (ReferenceEquals(e.Pointer.Captured, this) && e.Properties.IsLeftButtonPressed)
        {
            IsPressed = true;
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        if (ReferenceEquals(e.Pointer.Captured, this))
        {
            IsPressed = false;
        }
    }

    // The pointer taken away mid-press (another window, Alt+Tab) ends the press: no release will come to say so.
    protected override void OnPointerCaptureLost(PointerEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        IsPressed = false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!IsEnabled || e.IsRepeated) return;

        switch (e.Key)
        {
            case Key.Space:
                IsPressed = true;
                if (ClickMode == ClickMode.Press) OnClick();
                e.Handled = true;
                break;
            case Key.Enter:
                OnClick();
                e.Handled = true;
                break;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key != Key.Space) return;

        var wasPressed = IsPressed;
        IsPressed = false;
        if (wasPressed && IsEnabled && ClickMode == ClickMode.Release)
        {
            OnClick();
        }
        e.Handled = true;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ButtonAutomationPeer(this);
}
