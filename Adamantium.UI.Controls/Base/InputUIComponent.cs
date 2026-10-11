using Adamantium.Mathematics;
using Adamantium.UI.Controls.Adorners;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.Base;

public class InputUIComponent : MeasurableUIComponent, IInputComponent
{
    #region Routed events
    
    private bool _isLoaded;
    private bool _unloadSent;

    public static readonly RoutedEvent LoadedEvent = EventManager.RegisterRoutedEvent( nameof(Loaded),
        RoutingStrategy.Direct, typeof(RoutedEventHandler), typeof(UIComponent));
    
    public static readonly RoutedEvent UnloadedEvent = EventManager.RegisterRoutedEvent("Unloaded",
        RoutingStrategy.Direct, typeof(RoutedEventHandler), typeof(UIComponent));

    public static readonly RoutedEvent InitializedEvent = EventManager.RegisterRoutedEvent(nameof(Initialized),
        RoutingStrategy.Direct, typeof(RoutedEventHandler), typeof(UIComponent));

    #endregion

    // The ids of the pointers over this element, so an enter told twice for one pointer still takes one exit to undo.
    private List<uint> _pointersOver;
    private List<uint> _pointersDirectlyOver;
    
    static InputUIComponent()
    {
        FocusManager.GotFocusEvent.RegisterClassHandler<IInputComponent>(new RoutedEventHandler(GotFocusHandler));
        FocusManager.LostFocusEvent.RegisterClassHandler<IInputComponent>(new RoutedEventHandler(LostFocusHandler));
        Keyboard.GotKeyboardFocusWithinEvent.RegisterClassHandler<IInputComponent>(
            new RoutedEventHandler(GotKeyboardFocusWithinHandler));
        Keyboard.LostKeyboardFocusWithinEvent.RegisterClassHandler<IInputComponent>(
            new RoutedEventHandler(LostKeyboardFocusWithinHandler));
        FocusManager.FocusRevealedEvent.RegisterClassHandler<IInputComponent>(new RoutedEventHandler(FocusRevealedHandler));
        Keyboard.KeyDownEvent.RegisterClassHandler<IInputComponent>(new KeyEventHandler(KeyDownHandler));
        Keyboard.KeyUpEvent.RegisterClassHandler<IInputComponent>(new KeyEventHandler(KeyUpHandler));
        Keyboard.PreviewKeyDownEvent.RegisterClassHandler<IInputComponent>(new KeyEventHandler(PreviewKeyDownHandler));
        Keyboard.PreviewKeyUpEvent.RegisterClassHandler<IInputComponent>(new KeyEventHandler(PreviewKeyUpHandler));
        PointerEvents.PointerEnteredEvent.RegisterClassHandler<IInputComponent>(new PointerEventHandler(PointerEnteredHandler));
        PointerEvents.PointerExitedEvent.RegisterClassHandler<IInputComponent>(new PointerEventHandler(PointerExitedHandler));
        PointerEvents.DirectlyOverEnteredEvent.RegisterClassHandler<IInputComponent>(new PointerEventHandler(DirectlyOverEnteredHandler));
        PointerEvents.DirectlyOverExitedEvent.RegisterClassHandler<IInputComponent>(new PointerEventHandler(DirectlyOverExitedHandler));
        Keyboard.GotKeyboardFocusEvent.RegisterClassHandler<IInputComponent>(
            new KeyboardGotFocusEventHandler(GotKeyboardFocusHandler));
        Keyboard.LostKeyboardFocusEvent.RegisterClassHandler<IInputComponent>(
            new KeyboardFocusChangedEventHandler(LostKeyboardFocusHandler));
        PointerEvents.PreviewPointerPressedEvent.RegisterClassHandler<IInputComponent>(
            new PointerPressedEventHandler((sender, e) => (sender as InputUIComponent)?.OnPreviewPointerPressed(e)));
        PointerEvents.PointerPressedEvent.RegisterClassHandler<IInputComponent>(
            new PointerPressedEventHandler((sender, e) => (sender as InputUIComponent)?.OnPointerPressed(e)));
        PointerEvents.PreviewPointerReleasedEvent.RegisterClassHandler<IInputComponent>(
            new PointerReleasedEventHandler((sender, e) => (sender as InputUIComponent)?.OnPreviewPointerReleased(e)));
        PointerEvents.PointerReleasedEvent.RegisterClassHandler<IInputComponent>(
            new PointerReleasedEventHandler((sender, e) => (sender as InputUIComponent)?.OnPointerReleased(e)));
        PointerEvents.PointerMovedEvent.RegisterClassHandler<IInputComponent>(
            new PointerEventHandler((sender, e) => (sender as InputUIComponent)?.OnPointerMoved(e)));
        PointerEvents.PointerCaptureLostEvent.RegisterClassHandler<IInputComponent>(
            new PointerEventHandler((sender, e) => (sender as InputUIComponent)?.OnPointerCaptureLost(e)));
        Mouse.RawMouseMoveEvent.RegisterClassHandler<IInputComponent>(
            new RawMouseEventHandler((sender, e) => (sender as InputUIComponent)?.OnRawMouseMove(e)));

        PointerEvents.PointerReleasedEvent.RegisterClassHandler<IInputComponent>(new PointerReleasedEventHandler(OpenContextMenuHandler));
        Keyboard.KeyDownEvent.RegisterClassHandler<IInputComponent>(new KeyEventHandler(OpenContextMenuByKeyHandler));

        // The one place guaranteed to run before any element exists.
        KeyboardNavigation.Register();
        // An overlay element has no visual path back to its window; only the popup layer knows it.
        KeyboardNavigation.HostOf = element => Popup.HostOf(element);
    }

    private static void OpenContextMenuHandler(object sender, PointerReleasedEventArgs e)
    {
        if (e.Handled || e.ChangedButton != MouseButtons.Right || ContextMenuHost(sender as IUIComponent) is not { } host)
        {
            return;
        }

        host.ContextMenu.Open(host, e.GetPosition(host));
        e.Handled = true;
    }

    // The menu key, or Shift+F10.
    private static void OpenContextMenuByKeyHandler(object sender, KeyEventArgs e)
    {
        var shift = (e.Modifiers & (InputModifiers.LeftShift | InputModifiers.RightShift)) != 0;
        if (e.Handled || !(e.Key == Key.Apps || (e.Key == Key.F10 && shift))) return;

        if (OpenContextMenu(sender as IUIComponent)) e.Handled = true;
    }

    /// <summary>Opens the context menu of <paramref name="element"/>, or of the nearest element above it that has one,
    /// under <paramref name="element"/> and with the keyboard on its first row - what the menu key does. False when
    /// there is none.</summary>
    internal static bool OpenContextMenu(IUIComponent element)
    {
        if (ContextMenuHost(element) is not { } host) return false;

        host.ContextMenu.Open(host, element.TranslatePoint(new Vector2(0, element.RenderSize.Height), host));
        host.ContextMenu.MoveKeyboardInsideWhenReady();
        return true;
    }

    private static InputUIComponent ContextMenuHost(IUIComponent element)
    {
        for (var node = element; node != null; node = node.VisualParent)
        {
            if (node is InputUIComponent { ContextMenu: not null } host) return host;
        }

        return null;
    }

    public static readonly AdamantiumProperty IsFocusedProperty =
        AdamantiumProperty.RegisterReadOnly(nameof(IsFocused),
            typeof(Boolean), typeof(InputUIComponent), new PropertyMetadata(false));

    public static readonly AdamantiumProperty ToolTipProperty = AdamantiumProperty.Register(nameof(ToolTip),
        typeof(object), typeof(InputUIComponent), new PropertyMetadata(null, OnToolTipChanged));

    private static void OnToolTipChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is InputUIComponent c) ToolTipService.SetToolTip(c, e.NewValue);
    }

    /// <summary>A right-click flyout, opened at the cursor on right-button-up. A logical child, so it is themed and
    /// inherits DataContext.</summary>
    public static readonly AdamantiumProperty ContextMenuProperty = AdamantiumProperty.Register(nameof(ContextMenu),
        typeof(Adamantium.UI.Controls.ContextMenu), typeof(InputUIComponent), new PropertyMetadata(null, OnContextMenuChanged));

    public Adamantium.UI.Controls.ContextMenu ContextMenu
    {
        get => GetValue<Adamantium.UI.Controls.ContextMenu>(ContextMenuProperty);
        set => SetValue(ContextMenuProperty, value);
    }

    private static void OnContextMenuChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is not InputUIComponent host) return;
        if (e.OldValue is Adamantium.UI.Controls.ContextMenu old) host.RemoveLogicalChild(old);
        if (e.NewValue is Adamantium.UI.Controls.ContextMenu menu) host.AddLogicalChild(menu);
    }

    // False by default, as in WPF: interactive controls opt in, so a clicked template part never lands the focus on a
    // passive container.
    public static readonly AdamantiumProperty FocusableProperty = AdamantiumProperty.Register(nameof(Focusable),
        typeof(Boolean), typeof(InputUIComponent), new PropertyMetadata(false));
    
    public static readonly AdamantiumProperty IsKeyboardFocusedProperty =
        AdamantiumProperty.RegisterReadOnly(nameof(IsKeyboardFocused),
            typeof(Boolean), typeof(InputUIComponent), new PropertyMetadata(false));
    
    /// <summary>A pointer is over the element or one of its descendants: the mouse or a pen in range, or a finger down.</summary>
    public static readonly AdamantiumProperty IsPointerOverProperty =
        AdamantiumProperty.RegisterReadOnly(nameof(IsPointerOver),
            typeof(Boolean), typeof(InputUIComponent), new PropertyMetadata(false));

    /// <summary>The focus is on this element or inside it - what a composite control, whose editor holds the focus, asks.</summary>
    public static readonly AdamantiumProperty IsKeyboardFocusWithinProperty =
        AdamantiumProperty.RegisterReadOnly(nameof(IsKeyboardFocusWithin),
            typeof(Boolean), typeof(InputUIComponent), new PropertyMetadata(false));

    /// <summary>The focus got here by keyboard - when a focus ring is worth drawing.</summary>
    public static readonly AdamantiumProperty IsFocusVisibleProperty =
        AdamantiumProperty.RegisterReadOnly(nameof(IsFocusVisible),
            typeof(Boolean), typeof(InputUIComponent), new PropertyMetadata(false));

    /// <summary>A style for this control's <see cref="FocusAdorner"/>, applied after the theme's. Null leaves the theme's
    /// ring; a style with no Template shows none.</summary>
    public static readonly AdamantiumProperty FocusVisualStyleProperty = AdamantiumProperty.Register(
        nameof(FocusVisualStyle), typeof(Style), typeof(InputUIComponent), new PropertyMetadata(null));

    /// <summary>The part of this element the keyboard is on, in its own units, for the focus ring to go round - a link
    /// in a TextBlock; null rings the whole element.</summary>
    public virtual Rect? FocusBounds => null;

    public Style FocusVisualStyle
    {
        get => GetValue<Style>(FocusVisualStyleProperty);
        set => SetValue(FocusVisualStyleProperty, value);
    }

    /// <summary>A pointer is over the element itself, not one of its descendants.</summary>
    public static readonly AdamantiumProperty IsPointerDirectlyOverProperty =
        AdamantiumProperty.RegisterReadOnly(nameof(IsPointerDirectlyOver),
            typeof(Boolean), typeof(InputUIComponent), new PropertyMetadata(false));
    
    public static readonly AdamantiumProperty IsInitializedProperty =
        AdamantiumProperty.RegisterReadOnly(nameof(IsInitialized),
            typeof(Boolean), typeof(InputUIComponent), new PropertyMetadata(false, OnIsInitializedChanged));

    // Mirrored in a field: read per node on every attach walk, where a property read was the largest cost.
    private bool _isInitialized;

    /// <summary>A one-way latch: false until the component first joins a visual tree, true for ever after.</summary>
    public bool IsInitialized
    {
        get => _isInitialized;
        private set => SetValue(IsInitializedProperty, value);
    }
    
    public bool IsPointerOver
    {
        get => GetValue<bool>(IsPointerOverProperty);
        private set => SetValue(IsPointerOverProperty, value);
    }

    public bool IsKeyboardFocusWithin
    {
        get => GetValue<bool>(IsKeyboardFocusWithinProperty);
        private set => SetValue(IsKeyboardFocusWithinProperty, value);
    }

    public bool IsFocusVisible
    {
        get => GetValue<bool>(IsFocusVisibleProperty);
        private set => SetValue(IsFocusVisibleProperty, value);
    }

    public bool IsPointerDirectlyOver
    {
        get => GetValue<bool>(IsPointerDirectlyOverProperty);
        private set => SetValue(IsPointerDirectlyOverProperty, value);
    }

    
    public bool IsKeyboardFocused
    {
        get => GetValue<bool>(IsKeyboardFocusedProperty);
        private set => SetValue(IsKeyboardFocusedProperty, value);
    }
    

    /// <summary>Content shown as a hover tooltip - a string or any UI content. Bindable; shown by <see cref="ToolTipService"/>.</summary>
    public object ToolTip
    {
        get => GetValue<object>(ToolTipProperty);
        set => SetValue(ToolTipProperty, value);
    }

    public Boolean IsFocused
    {
        get => GetValue<Boolean>(IsFocusedProperty);
        private set => SetValue(IsFocusedProperty, value);
    }
    
    public Boolean Focusable
    {
        get => GetValue<Boolean>(FocusableProperty);
        set => SetValue(FocusableProperty, value);
    }
    
    public event RoutedEventHandler Loaded
    {
        add => AddHandler(LoadedEvent, value);
        remove => RemoveHandler(LoadedEvent, value);
    }
    
    public event RoutedEventHandler Unloaded
    {
        add => AddHandler(UnloadedEvent, value);
        remove => RemoveHandler(UnloadedEvent, value);
    }
    
    public event RoutedEventHandler Initialized
    {
        add => AddHandler(InitializedEvent, value);
        remove => RemoveHandler(InitializedEvent, value);
    }

    public event RawMouseEventHandler RawMouseMove
    {
        add => AddHandler(Mouse.RawMouseMoveEvent, value);
        remove => RemoveHandler(Mouse.RawMouseMoveEvent, value);
    }

    public event RoutedEventHandler GotFocus
    {
        add => AddHandler(FocusManager.GotFocusEvent, value);
        remove => RemoveHandler(FocusManager.GotFocusEvent, value);
    }

    public event RoutedEventHandler LostFocus
    {
        add => AddHandler(FocusManager.LostFocusEvent, value);
        remove => RemoveHandler(FocusManager.LostFocusEvent, value);
    }

    public event KeyEventHandler KeyDown
    {
        add => AddHandler(Keyboard.KeyDownEvent, value);
        remove => RemoveHandler(Keyboard.KeyDownEvent, value);
    }

    public event KeyEventHandler KeyUp
    {
        add => AddHandler(Keyboard.KeyUpEvent, value);
        remove => RemoveHandler(Keyboard.KeyUpEvent, value);
    }

    public event KeyboardGotFocusEventHandler GotKeyboardFocus
    {
        add => AddHandler(Keyboard.GotKeyboardFocusEvent, value);
        remove => RemoveHandler(Keyboard.GotKeyboardFocusEvent, value);
    }

    public event KeyboardFocusChangedEventHandler LostKeyboardFocus
    {
        add => AddHandler(Keyboard.LostKeyboardFocusEvent, value);
        remove => RemoveHandler(Keyboard.LostKeyboardFocusEvent, value);
    }

    public event PointerEventHandler GotPointerCapture
    {
        add => AddHandler(PointerEvents.GotPointerCaptureEvent, value);
        remove => RemoveHandler(PointerEvents.GotPointerCaptureEvent, value);
    }

    public event PointerEventHandler PointerCaptureLost
    {
        add => AddHandler(PointerEvents.PointerCaptureLostEvent, value);
        remove => RemoveHandler(PointerEvents.PointerCaptureLostEvent, value);
    }

    public event PointerEventHandler PointerEntered
    {
        add => AddHandler(PointerEvents.PointerEnteredEvent, value);
        remove => RemoveHandler(PointerEvents.PointerEnteredEvent, value);
    }

    public event PointerEventHandler PointerExited
    {
        add => AddHandler(PointerEvents.PointerExitedEvent, value);
        remove => RemoveHandler(PointerEvents.PointerExitedEvent, value);
    }

    public event PointerEventHandler PointerMoved
    {
        add => AddHandler(PointerEvents.PointerMovedEvent, value);
        remove => RemoveHandler(PointerEvents.PointerMovedEvent, value);
    }

    public event PointerWheelEventHandler PointerWheelChanged
    {
        add => AddHandler(PointerEvents.PointerWheelChangedEvent, value);
        remove => RemoveHandler(PointerEvents.PointerWheelChangedEvent, value);
    }

    public event PointerEventHandler PointerCanceled
    {
        add => AddHandler(PointerEvents.PointerCanceledEvent, value);
        remove => RemoveHandler(PointerEvents.PointerCanceledEvent, value);
    }

    public event DragDropEventHandler DragEnter
    {
        add => AddHandler(DragDropEvents.DragEnterEvent, value);
        remove => RemoveHandler(DragDropEvents.DragEnterEvent, value);
    }

    public event DragDropEventHandler DragOver
    {
        add => AddHandler(DragDropEvents.DragOverEvent, value);
        remove => RemoveHandler(DragDropEvents.DragOverEvent, value);
    }

    public event DragDropEventHandler DragLeave
    {
        add => AddHandler(DragDropEvents.DragLeaveEvent, value);
        remove => RemoveHandler(DragDropEvents.DragLeaveEvent, value);
    }

    public event DragDropEventHandler Drop
    {
        add => AddHandler(DragDropEvents.DropEvent, value);
        remove => RemoveHandler(DragDropEvents.DropEvent, value);
    }

    public event DragDropEventHandler PreviewDragEnter
    {
        add => AddHandler(DragDropEvents.PreviewDragEnterEvent, value);
        remove => RemoveHandler(DragDropEvents.PreviewDragEnterEvent, value);
    }

    public event DragDropEventHandler PreviewDragOver
    {
        add => AddHandler(DragDropEvents.PreviewDragOverEvent, value);
        remove => RemoveHandler(DragDropEvents.PreviewDragOverEvent, value);
    }

    public event DragDropEventHandler PreviewDragLeave
    {
        add => AddHandler(DragDropEvents.PreviewDragLeaveEvent, value);
        remove => RemoveHandler(DragDropEvents.PreviewDragLeaveEvent, value);
    }

    public event DragDropEventHandler PreviewDrop
    {
        add => AddHandler(DragDropEvents.PreviewDropEvent, value);
        remove => RemoveHandler(DragDropEvents.PreviewDropEvent, value);
    }

    public event PointerPressedEventHandler PointerPressed
    {
        add => AddHandler(PointerEvents.PointerPressedEvent, value);
        remove => RemoveHandler(PointerEvents.PointerPressedEvent, value);
    }

    public event PointerReleasedEventHandler PointerReleased
    {
        add => AddHandler(PointerEvents.PointerReleasedEvent, value);
        remove => RemoveHandler(PointerEvents.PointerReleasedEvent, value);
    }

    public event TextInputEventHandler TextInput
    {
        add => AddHandler(Keyboard.TextInputEvent, value);
        remove => RemoveHandler(Keyboard.TextInputEvent, value);
    }


    public event KeyEventHandler PreviewKeyDown
    {
        add => AddHandler(Keyboard.PreviewKeyDownEvent, value);
        remove => RemoveHandler(Keyboard.PreviewKeyDownEvent, value);
    }

    public event KeyEventHandler PreviewKeyUp
    {
        add => AddHandler(Keyboard.PreviewKeyUpEvent, value);
        remove => RemoveHandler(Keyboard.PreviewKeyUpEvent, value);
    }

    public event KeyboardGotFocusEventHandler PreviewGotKeyboardFocus
    {
        add => AddHandler(Keyboard.PreviewGotKeyboardFocusEvent, value);
        remove => RemoveHandler(Keyboard.PreviewGotKeyboardFocusEvent, value);
    }

    public event KeyboardFocusChangedEventHandler PreviewLostKeyboardFocus
    {
        add => AddHandler(Keyboard.PreviewLostKeyboardFocusEvent, value);
        remove => RemoveHandler(Keyboard.PreviewLostKeyboardFocusEvent, value);
    }

    public event PointerPressedEventHandler PreviewPointerPressed
    {
        add => AddHandler(PointerEvents.PreviewPointerPressedEvent, value);
        remove => RemoveHandler(PointerEvents.PreviewPointerPressedEvent, value);
    }

    public event PointerReleasedEventHandler PreviewPointerReleased
    {
        add => AddHandler(PointerEvents.PreviewPointerReleasedEvent, value);
        remove => RemoveHandler(PointerEvents.PreviewPointerReleasedEvent, value);
    }

    public event PointerWheelEventHandler PreviewPointerWheelChanged
    {
        add => AddHandler(PointerEvents.PreviewPointerWheelChangedEvent, value);
        remove => RemoveHandler(PointerEvents.PreviewPointerWheelChangedEvent, value);
    }

    public event PointerEventHandler PreviewPointerMoved
    {
        add => AddHandler(PointerEvents.PreviewPointerMovedEvent, value);
        remove => RemoveHandler(PointerEvents.PreviewPointerMovedEvent, value);
    }

    public event TextInputEventHandler PreviewTextInput
    {
        add => AddHandler(Keyboard.PreviewTextInputEvent, value);
        remove => RemoveHandler(Keyboard.PreviewTextInputEvent, value);
    }

    private static void GotFocusHandler(object sender, RoutedEventArgs e)
    {
        var ui = sender as InputUIComponent;
        ui?.OnGotFocus(e);
    }

    private static void LostFocusHandler(object sender, RoutedEventArgs e)
    {
        var ui = sender as InputUIComponent;
        ui?.OnLostFocus(e);
    }

    // Subscribed once and handed to a virtual: a control overrides OnKeyDown instead of registering a class handler.
    private static void KeyDownHandler(object sender, KeyEventArgs e)
    {
        (sender as InputUIComponent)?.OnKeyDown(e);
    }

    private static void KeyUpHandler(object sender, KeyEventArgs e)
    {
        (sender as InputUIComponent)?.OnKeyUp(e);
    }

    private static void PreviewKeyDownHandler(object sender, KeyEventArgs e)
    {
        (sender as InputUIComponent)?.OnPreviewKeyDown(e);
    }

    private static void PreviewKeyUpHandler(object sender, KeyEventArgs e)
    {
        (sender as InputUIComponent)?.OnPreviewKeyUp(e);
    }

    /// <summary>A key bubbling through this element. Handled claims it; navigation gets only the keys nobody claimed.</summary>
    protected virtual void OnKeyDown(KeyEventArgs e)
    {
    }

    protected virtual void OnKeyUp(KeyEventArgs e)
    {
    }

    /// <summary>The key on the way down, before anything inside sees it - how a composite control takes a key its own
    /// editor would claim first.</summary>
    protected virtual void OnPreviewKeyDown(KeyEventArgs e)
    {
    }

    protected virtual void OnPreviewKeyUp(KeyEventArgs e)
    {
    }

    // Raised on each element that joined or left the focused chain, so the state is simply set.
    private static void GotKeyboardFocusWithinHandler(object sender, RoutedEventArgs e)
    {
        if (sender is not InputUIComponent ui) return;
        ui.IsKeyboardFocusWithin = true;
        ui.IsFocusVisible = FocusManager.IsFocusVisible;
    }

    private static void LostKeyboardFocusWithinHandler(object sender, RoutedEventArgs e)
    {
        if (sender is not InputUIComponent ui) return;
        ui.IsKeyboardFocusWithin = false;
        ui.IsFocusVisible = false;
    }

    private static void FocusRevealedHandler(object sender, RoutedEventArgs e)
    {
        if (sender is not InputUIComponent ui)
        {
            return;
        }

        ui.IsFocusVisible = true;
        if (ui.IsFocused)
        {
            ui.ShowFocusRing();
        }
    }

    private static void PointerEnteredHandler(object sender, PointerEventArgs e)
    {
        if (sender is not InputUIComponent ui || !Add(ref ui._pointersOver, e.Pointer.Id))
        {
            return;
        }

        ui.IsPointerOver = true;
        ui.OnPointerEntered(e);
    }

    private static void PointerExitedHandler(object sender, PointerEventArgs e)
    {
        if (sender is not InputUIComponent ui || ui._pointersOver?.Remove(e.Pointer.Id) != true)
        {
            return;
        }

        ui.IsPointerOver = ui._pointersOver.Count > 0;
        ui.OnPointerExited(e);
    }

    private static void DirectlyOverEnteredHandler(object sender, PointerEventArgs e)
    {
        if (sender is InputUIComponent ui && Add(ref ui._pointersDirectlyOver, e.Pointer.Id))
        {
            ui.IsPointerDirectlyOver = true;
        }
    }

    private static void DirectlyOverExitedHandler(object sender, PointerEventArgs e)
    {
        if (sender is InputUIComponent ui && ui._pointersDirectlyOver?.Remove(e.Pointer.Id) == true)
        {
            ui.IsPointerDirectlyOver = ui._pointersDirectlyOver.Count > 0;
        }
    }

    private static bool Add(ref List<uint> ids, uint id)
    {
        ids ??= new List<uint>(1);
        if (ids.Contains(id))
        {
            return false;
        }

        ids.Add(id);
        return true;
    }

    private static void GotKeyboardFocusHandler(object sender, KeyboardGotFocusEventArgs e)
    {
        if (sender is InputUIComponent ui && ReferenceEquals(e.OriginalSource, ui))
        {
            ui.IsKeyboardFocused = true;
        }
    }

    private static void LostKeyboardFocusHandler(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is InputUIComponent ui && ReferenceEquals(e.OriginalSource, ui))
        {
            ui.IsKeyboardFocused = false;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
         base.OnAttachedToVisualTree(e);
         _unloadSent = false;

         // A one-way latch: rewriting it per attach cost a property-system trip per node.
         if (!IsInitialized) IsInitialized = true;
    }

    internal void RaiseUnloaded()
    {
        if (_unloadSent)
        {
            return;
        }

        _unloadSent = true;
        RaiseEvent(new RoutedEventArgs(UnloadedEvent, this));
    }

    /// <summary>Also lets go of the context menu, which is not in the visual tree: one of its own goes with it, and one a
    /// style shares among elements is only unhooked from this one.</summary>
    protected override void OnDiscarded()
    {
        if (_isLoaded)
        {
            RaiseUnloaded();
        }

        base.OnDiscarded();

        if (ContextMenu is not { } menu || !ReferenceEquals(menu.LogicalParent, this))
        {
            return;
        }

        if (GetBaseValuePriority(ContextMenuProperty) == ValuePriority.Local)
        {
            DiscardedVisuals.Publish(menu);
        }
        else
        {
            RemoveLogicalChild(menu);
        }
    }

    public IWindow GetWindow()
    {
        if (!IsInitialized) return null;

        return this.GetSelfAndLogicalAncestors().OfType<IWindow>().FirstOrDefault();
    }
    
    private static void OnIsInitializedChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is not InputUIComponent ui) return;

        // Resolved value, not e.NewValue: a trigger exit writes UnsetValue.
        ui._isInitialized = ui.GetValue<bool>(IsInitializedProperty);

        if (!ui._isInitialized) return;

        ui.RaiseEvent(new RoutedEventArgs(InitializedEvent));
        ui.OnInitialized();
    }

    protected virtual void OnInitialized()
    {
        
    }

    /// <summary>The mouse itself moved while this element has the focus, unbound by the screen's edges.</summary>
    protected virtual void OnRawMouseMove(UnboundMouseEventArgs e)
    {
    }

    /// <summary>A pointer moved over this element, or anywhere while this element holds it.</summary>
    protected virtual void OnPointerMoved(PointerEventArgs e)
    {
    }

    /// <summary>A pointer is pressing, on the way down to the element it hit.</summary>
    protected virtual void OnPreviewPointerPressed(PointerPressedEventArgs e)
    {
    }

    /// <summary>A pointer pressed on this element or inside it: a mouse button (see
    /// <see cref="PointerPressedEventArgs.ChangedButton"/>), a finger or a pen tip. Not called once an element inside
    /// has handled the press; an element that must see it anyway adds a handler with handledEventsToo.</summary>
    protected virtual void OnPointerPressed(PointerPressedEventArgs e)
    {
    }

    /// <summary>A pointer is letting go, on the way down to the element it hit.</summary>
    protected virtual void OnPreviewPointerReleased(PointerReleasedEventArgs e)
    {
    }

    /// <summary>A pointer let go over this element or inside it, or anywhere while this element holds it.</summary>
    protected virtual void OnPointerReleased(PointerReleasedEventArgs e)
    {
    }

    /// <summary>This element no longer holds a pointer it held.</summary>
    protected virtual void OnPointerCaptureLost(PointerEventArgs e)
    {
    }

    protected virtual void OnGotFocus(RoutedEventArgs e)
    {
        IsFocused = e.OriginalSource == this;
        if (IsFocused && FocusManager.IsFocusVisible)
        {
            ShowFocusRing();
        }
    }

    private void ShowFocusRing()
    {
        AdornerHost()?.AdornerLayer.SetFocus(FocusVisualOwner());
        // Keyboard only: scrolling under a click would move the target from under the cursor.
        if (!FocusManager.IsReturning)
        {
            (FocusVisualOwner() as UIComponent)?.BringIntoView();
        }
    }

    private InputUIComponent FocusVisualOwner()
    {
        var owner = this;
        while (owner.TemplatedParent is TemplatedUIComponent templated && HasOneStop(templated))
        {
            owner = templated;
        }

        return owner;
    }

    private static bool HasOneStop(InputUIComponent control)
    {
        var stops = 0;
        foreach (var node in control.GetVisualDescendants())
        {
            if (node is InputUIComponent { Focusable: true, Visibility: Visibility.Visible } part &&
                ReferenceEquals(part.TemplatedParent, control) &&
                KeyboardNavigation.GetIsTabStop(part) &&
                ++stops > 1)
            {
                return false;
            }
        }

        return true;
    }

    protected virtual void OnLostFocus(RoutedEventArgs e)
    {
        IsFocused = false;
        // Lost comes before Got, so exactly one ring remains.
        AdornerHost()?.AdornerLayer.SetFocus(null);
    }

    // The window's adorner layer, not the template: one implementation for every control, drawn above any clipping.
    private IAdornerHost AdornerHost()
    {
        for (IUIComponent node = this; node != null; node = node.VisualParent)
        {
            if (node is IAdornerHost host)
                return host;
        }

        // On the overlay: no visual path back, so the popup names its window.
        return Popup.HostOf(this) as IAdornerHost;
    }

    /// <summary>Leaving the tree gives up the focus, or Tab would keep walking a tree that is off screen.</summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        FocusManager.Release(this);
    }

    /// <summary>A pointer came over this element or one of its descendants. The mouse and a pen show this element's
    /// cursor.</summary>
    protected virtual void OnPointerEntered(PointerEventArgs e)
    {
        if (e.Pointer.Type != PointerType.Touch)
        {
            Mouse.Cursor = Cursor;
        }
    }

    /// <summary>A pointer is no longer over this element or any of its descendants. Back over the parent, which raises
    /// no enter, the parent's cursor returns; a captured drag dresses the pointer itself.</summary>
    protected virtual void OnPointerExited(PointerEventArgs e)
    {
        if (e.Pointer.Type == PointerType.Touch || ReferenceEquals(e.Pointer.Captured, this) || VisualParent is not UIComponent parent)
        {
            return;
        }

        Mouse.Cursor = parent.Cursor;
    }

    /// <summary>True while this element holds a pointer.</summary>
    public bool IsPointerCaptured
    {
        get
        {
            foreach (var pointer in PointerDevice.Current.Pointers)
            {
                if (ReferenceEquals(pointer.Captured, this))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Lets go of every pointer this element holds.</summary>
    public void ReleasePointerCaptures()
    {
        foreach (var pointer in PointerDevice.Current.Pointers.ToArray())
        {
            if (ReferenceEquals(pointer.Captured, this))
            {
                pointer.Capture(null);
            }
        }
    }

    public bool Focus()
    {
        return FocusManager.Focus(this);
    }

    protected override void OnRenderCompleted()
    {
        base.OnRenderCompleted();
        if (!_isLoaded)
        {
            _isLoaded = true; 
            RaiseEvent( new RoutedEventArgs(LoadedEvent, this));
        }
    }
}