using Adamantium.Mathematics;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Dispatcher;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls;

/// <summary>Shows one hover tooltip at a time for elements with <see cref="ToolTipProperty"/> set, after
/// <see cref="InitialShowDelayProperty"/>; leaving the element hides it.</summary>
public static class ToolTipService
{
    public static readonly AdamantiumProperty ToolTipProperty = AdamantiumProperty.RegisterAttached(
        "ToolTip", typeof(object), typeof(AdamantiumComponent), new PropertyMetadata(null, OnToolTipChanged));

    /// <summary>Hover delay before the tooltip appears, in milliseconds (default 500).</summary>
    public static readonly AdamantiumProperty InitialShowDelayProperty = AdamantiumProperty.RegisterAttached(
        "InitialShowDelay", typeof(double), typeof(AdamantiumComponent), new PropertyMetadata(500.0));

    /// <summary>Where the card sits relative to the element (default <see cref="PlacementMode.Bottom"/>).</summary>
    public static readonly AdamantiumProperty PlacementProperty = AdamantiumProperty.RegisterAttached(
        "Placement", typeof(PlacementMode), typeof(AdamantiumComponent), new PropertyMetadata(PlacementMode.Bottom));

    /// <summary>Whether the element takes hover and shows its tooltip though it or an ancestor is disabled - the "why"
    /// beside something that cannot be used (default false).</summary>
    public static readonly AdamantiumProperty ShowOnDisabledProperty = DisabledHover.ShowOnDisabledProperty;

    /// <summary>Tooltips of the element's parts (<see cref="IToolTipRegions"/>): the card shows under the part the pointer
    /// rests on, after <see cref="InitialShowDelayProperty"/>, and goes as the pointer leaves the part. Set, it takes the
    /// place of <see cref="ToolTipProperty"/> on the element.</summary>
    public static readonly AdamantiumProperty ToolTipRegionsProperty = AdamantiumProperty.RegisterAttached(
        "ToolTipRegions", typeof(IToolTipRegions), typeof(AdamantiumComponent),
        new PropertyMetadata(null, OnToolTipRegionsChanged));

    public static IToolTipRegions GetToolTipRegions(AdamantiumComponent element) =>
        element.GetValue<IToolTipRegions>(ToolTipRegionsProperty);

    public static void SetToolTipRegions(AdamantiumComponent element, IToolTipRegions value) =>
        element.SetValue(ToolTipRegionsProperty, value);

    /// <summary>Asks the parts of <paramref name="element"/> again where the pointer rests on it: for what is under a
    /// still pointer that moved - a page scrolled by the wheel.</summary>
    public static void RefreshToolTipRegions(IInputComponent element)
    {
        if (element != null && ReferenceEquals(element, _regionsUnder) && element.IsPointerOver)
        {
            AskRegions(element, _regionsPoint);
        }
    }

    public static bool GetShowOnDisabled(AdamantiumComponent element) => element.GetValue<bool>(ShowOnDisabledProperty);
    public static void SetShowOnDisabled(AdamantiumComponent element, bool value) => element.SetValue(ShowOnDisabledProperty, value);

    public static object GetToolTip(AdamantiumComponent element) => element.GetValue(ToolTipProperty);
    public static void SetToolTip(AdamantiumComponent element, object value) => element.SetValue(ToolTipProperty, value);

    public static double GetInitialShowDelay(AdamantiumComponent element) => element.GetValue<double>(InitialShowDelayProperty);
    public static void SetInitialShowDelay(AdamantiumComponent element, double value) => element.SetValue(InitialShowDelayProperty, value);

    public static PlacementMode GetPlacement(AdamantiumComponent element) => element.GetValue<PlacementMode>(PlacementProperty);
    public static void SetPlacement(AdamantiumComponent element, PlacementMode value) => element.SetValue(PlacementProperty, value);

    private const double CardGap = 4;
    private const double UnderPointer = 20;

    // Shared, one tooltip at a time (like WPF's PopupControlService). Built lazily - on the first show, or as an
    // element's parts are set - so nothing is created for an app that never uses a tooltip.
    private static Popup _popup;
    private static ToolTip _toolTip;
    private static DispatcherTimer _timer;
    private static IInputComponent _pending;   // element awaiting show, or the one currently shown
    private static bool _inRegion;
    private static object _regionTip;
    private static Rect _region;
    private static IInputComponent _regionsUnder;
    private static Vector2<double> _regionsPoint;
    private static IInputComponent _quietOwner;
    private static Rect _quietRegion;

    private static void OnToolTipRegionsChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is not IInputComponent input)
        {
            return;
        }

        input.RemoveHandler(PointerEvents.PointerMovedEvent, RegionsMoved);
        input.PointerExited -= OnRegionsPointerExited;
        if (e.NewValue != null)
        {
            WatchPresses();
            EnsureBuilt();
            input.AddHandler(PointerEvents.PointerMovedEvent, RegionsMoved, handledEventsToo: true);
            input.PointerExited += OnRegionsPointerExited;
            return;
        }

        LetRegionsGo(input);
        if (ReferenceEquals(_pending, input))
        {
            Hide();
        }
    }

    private static readonly PointerEventHandler RegionsMoved = OnRegionsPointerMoved;

    private static void OnRegionsPointerMoved(object sender, PointerEventArgs e)
    {
        if (sender is not IInputComponent input)
        {
            return;
        }

        var held = (e.Modifiers & PressedButtons) != 0;
        if (held || HasTipOfItsOwn(e.OriginalSource as IUIComponent, input))
        {
            if (held)
            {
                _regionsUnder = null;
            }
            else
            {
                LetRegionsGo(input);
            }

            if (ReferenceEquals(_pending, input))
            {
                Hide();
            }

            return;
        }

        AskRegions(input, e.GetPosition(input));
    }

    private const InputModifiers PressedButtons = InputModifiers.LeftMouseButton | InputModifiers.RightMouseButton |
                                                InputModifiers.MiddleMouseButton | InputModifiers.X1MouseButton |
                                                InputModifiers.X2MouseButton;

    private static void LetRegionsGo(IInputComponent input)
    {
        if (ReferenceEquals(_regionsUnder, input))
        {
            _regionsUnder = null;
        }

        if (ReferenceEquals(_quietOwner, input))
        {
            _quietOwner = null;
        }
    }

    private static bool HasTipOfItsOwn(IUIComponent from, IInputComponent owner)
    {
        for (var node = from; node != null && !ReferenceEquals(node, owner); node = node.VisualParent)
        {
            if (node is AdamantiumComponent component &&
                (GetToolTip(component) != null || GetToolTipRegions(component) != null))
            {
                return true;
            }
        }

        return false;
    }

    private static void OnRegionsPointerExited(object sender, PointerEventArgs e)
    {
        if (sender is not IInputComponent { IsPointerOver: false } input)
        {
            return;
        }

        LetRegionsGo(input);
        if (ReferenceEquals(_pending, input))
        {
            Hide();
        }
    }

    private static void AskRegions(IInputComponent input, Vector2<double> point)
    {
        _regionsUnder = input;
        _regionsPoint = point;
        if (GetToolTipRegions((AdamantiumComponent)input) is not { } regions ||
            !regions.TryGetToolTipAt(point, out var tip, out var region) ||
            (tip is not IUIComponent && string.IsNullOrEmpty(tip?.ToString())))
        {
            _quietOwner = null;
            if (ReferenceEquals(_pending, input))
            {
                Hide();
            }

            return;
        }

        if (ReferenceEquals(_quietOwner, input) && region == _quietRegion)
        {
            return;
        }

        _quietOwner = null;
        if (ReferenceEquals(_pending, input) && _inRegion && region == _region && Equals(tip, _regionTip))
        {
            return;
        }

        if (_pending != null)
        {
            Hide();
        }

        _pending = input;
        _inRegion = true;
        _region = region;
        _regionTip = tip;
        EnsureBuilt();
        _timer.Stop();
        _timer.Start(TimeSpan.FromMilliseconds(GetInitialShowDelay((AdamantiumComponent)input)));
    }

    private static void OnToolTipChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is not IInputComponent input) return;

        // Idempotent: (re)wire hover only while a tooltip is set. Unsubscribe first so re-setting doesn't double-hook.
        input.PointerEntered -= OnPointerEntered;
        input.PointerExited -= OnPointerExited;
        if (e.NewValue != null)
        {
            WatchPresses();
            input.PointerEntered += OnPointerEntered;
            input.PointerExited += OnPointerExited;
        }
        else if (ReferenceEquals(_pending, input))
        {
            Hide();   // tooltip cleared off the element it was showing for
        }
    }

    private static void OnPointerEntered(object sender, PointerEventArgs e)
    {
        if (sender is not IInputComponent input || GetToolTip((AdamantiumComponent)input) == null ||
            GetToolTipRegions((AdamantiumComponent)input) != null) return;
        if (_inRegion) Hide();
        _pending = input;
        _inRegion = false;
        EnsureBuilt();
        _timer.Stop();
        _timer.Start(TimeSpan.FromMilliseconds(GetInitialShowDelay((AdamantiumComponent)input)));
    }

    private static void OnPointerExited(object sender, PointerEventArgs e)
    {
        // Only once nothing is over the owner: one pointer leaving while another stays is not leaving it.
        if (ReferenceEquals(sender, _pending) && !_pending.IsPointerOver) Hide();
    }

    private static bool _watchingPresses;

    private static void WatchPresses()
    {
        if (_watchingPresses) return;

        _watchingPresses = true;
        PointerEvents.PreviewPointerPressedEvent.RegisterClassHandler<IInputComponent>(
            new PointerPressedEventHandler(OnPointerPressed), handledEventsToo: true);
    }

    // A press acts on the element: its tip, up or still to come, goes, and comes again only on the next hover - a
    // button that drops a menu had its tip drawn over the menu. One shown on disabled is under a press its disabled
    // ancestor takes.
    private static void OnPointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (!ReferenceEquals(sender, e.OriginalSource) || _pending is not IUIComponent owner ||
            e.OriginalSource is not IUIComponent pressed) return;

        if (!IsWithin(pressed, owner) && !IsWithin(owner, pressed)) return;

        if (_inRegion)
        {
            _quietOwner = _pending;
            _quietRegion = _region;
        }

        Hide();
    }

    private static bool IsWithin(IUIComponent node, IUIComponent ancestor)
    {
        for (; node != null; node = node.VisualParent)
        {
            if (ReferenceEquals(node, ancestor)) return true;
        }

        return false;
    }

    // A menu or a list dropped open takes the card of anything outside it away.
    internal static void FlyoutOpened()
    {
        if (_pending is IUIComponent owner && Popup.IsBeneathOpenFlyout(owner)) Hide();
    }

    private static void OnTimerTick(object sender, EventArgs e)
    {
        _timer.Stop();   // one-shot: fire once after the delay
        if (_pending is not { IsPointerOver: true }) return;   // pointer left during the delay

        Show(_pending);
    }

    private static double VerticalScaleOf(IInputComponent owner)
    {
        var world = owner.WorldTransform;
        var top = Vector3F.TransformCoordinate(new Vector3F(0, 0, 0), world);
        var bottom = Vector3F.TransformCoordinate(new Vector3F(0, 1, 0), world);
        var scale = Vector3F.Distance(top, bottom);
        return scale > 1e-6 ? scale : 1;
    }

    internal static void Show(IInputComponent owner)
    {
        // A tip that reads as nothing - a command with no label and no key - is no card at all.
        var regional = _inRegion && ReferenceEquals(owner, _pending);
        var tip = regional ? _regionTip : GetToolTip((AdamantiumComponent)owner);
        if ((tip is not IUIComponent && string.IsNullOrEmpty(tip?.ToString())) ||
            (owner is IUIComponent component && Popup.IsBeneathOpenFlyout(component)))
        {
            Hide();
            return;
        }

        _pending = owner;
        EnsureBuilt();

        // The themed ToolTip control renders both a string (via its template's ContentPresenter) and a UI element as-is.
        _toolTip.Content = tip;

        var placement = regional ? PlacementMode.Relative : GetPlacement((AdamantiumComponent)owner);
        if (_popup.IsOpen && (_popup.Placement != placement || !ReferenceEquals(_popup.PlacementTarget, owner)))
        {
            _popup.IsOpen = false;
        }

        _popup.PlacementTarget = owner as UIComponent;
        _popup.HorizontalOffset = regional ? _regionsPoint.X : 0;
        _popup.VerticalOffset = regional ? _regionsPoint.Y + UnderPointer / VerticalScaleOf(owner) : CardGap;
        _popup.Placement = placement;
        _popup.DataContext = (owner as UIComponent)?.DataContext;   // so bindings in a content tooltip resolve
        _popup.IsOpen = true;
    }

    // EMPTIED as well as closed: the card is shared and lives for good, and what it last showed - the tip and the
    // element it was for - would live as long.
    internal static void Hide()
    {
        _timer?.Stop();
        _pending = null;
        _inRegion = false;
        _regionTip = null;
        _region = Rect.Empty;

        if (_popup == null) return;

        _popup.IsOpen = false;
        _popup.PlacementTarget = null;
        _popup.DataContext = null;
        _toolTip.Content = null;

        // MEASURED to hand the tip back: a presenter lets its content go as it is measured, and a closed card never is.
        _toolTip.Measure(new Adamantium.Mathematics.Size<double>(0, 0), force: true);
    }

    // An element thrown away while its tip is up takes the tip down: nothing else would, and the card would go on
    // showing what has been let go of.
    private static void OnDiscarded(ReadOnlySpan<IFundamentalUIComponent> gone)
    {
        foreach (var component in gone)
        {
            if (ReferenceEquals(component, _regionsUnder) || ReferenceEquals(component, _quietOwner))
            {
                _regionsUnder = null;
                _quietOwner = null;
            }
        }

        if (_pending == null) return;

        var tip = _toolTip.Content;
        foreach (var component in gone)
        {
            if (ReferenceEquals(component, _pending) || ReferenceEquals(component, tip))
            {
                Hide();
                return;
            }
        }
    }

    private static void EnsureBuilt()
    {
        if (_popup != null) return;

        // The card's entire look is themed (the ToolTip style); the service only fills its Content and positions it.
        //
        // NOT A TARGET. A tooltip is shown NEXT TO the pointer and often under it, and one that answers the mouse takes
        // both the hover and the press away from the control it is describing: the control's highlight blinked as the
        // card came and went, and a click that landed while it was up did nothing at all.
        _toolTip = new ToolTip { IsHitTestVisible = false };
        // Neither a press elsewhere nor Escape is the card's: the service takes it away as the hover ends, and an Escape
        // it answered was one an open menu under it never had.
        _popup = new Popup { Child = _toolTip, VerticalOffset = CardGap, KeepOpen = true, DismissOnEscape = false };

        _timer = new DispatcherTimer();
        _timer.Tick += OnTimerTick;

        DiscardedVisuals.Discarded += OnDiscarded;
    }
}
