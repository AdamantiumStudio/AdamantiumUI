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

    public static object GetToolTip(AdamantiumComponent element) => element.GetValue(ToolTipProperty);
    public static void SetToolTip(AdamantiumComponent element, object value) => element.SetValue(ToolTipProperty, value);

    public static double GetInitialShowDelay(AdamantiumComponent element) => element.GetValue<double>(InitialShowDelayProperty);
    public static void SetInitialShowDelay(AdamantiumComponent element, double value) => element.SetValue(InitialShowDelayProperty, value);

    public static PlacementMode GetPlacement(AdamantiumComponent element) => element.GetValue<PlacementMode>(PlacementProperty);
    public static void SetPlacement(AdamantiumComponent element, PlacementMode value) => element.SetValue(PlacementProperty, value);

    // Shared, one tooltip at a time (like WPF's PopupControlService). Built lazily on the first show so nothing is
    // created for an app that never uses a tooltip, and so the controls are created on the UI thread.
    private static Popup _popup;
    private static ToolTip _toolTip;
    private static DispatcherTimer _timer;
    private static IInputComponent _pending;   // element awaiting show, or the one currently shown

    private static void OnToolTipChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is not IInputComponent input) return;

        // Idempotent: (re)wire hover only while a tooltip is set. Unsubscribe first so re-setting doesn't double-hook.
        input.PointerEntered -= OnPointerEntered;
        input.PointerExited -= OnPointerExited;
        if (e.NewValue != null)
        {
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
        if (sender is not IInputComponent input || GetToolTip((AdamantiumComponent)input) == null) return;
        _pending = input;
        EnsureBuilt();
        _timer.Stop();
        _timer.Start(TimeSpan.FromMilliseconds(GetInitialShowDelay((AdamantiumComponent)input)));
    }

    private static void OnPointerExited(object sender, PointerEventArgs e)
    {
        // Only once nothing is over the owner: one pointer leaving while another stays is not leaving it.
        if (ReferenceEquals(sender, _pending) && !_pending.IsPointerOver) Hide();
    }

    private static void OnTimerTick(object sender, EventArgs e)
    {
        _timer.Stop();   // one-shot: fire once after the delay
        if (_pending is not { IsPointerOver: true }) return;   // pointer left during the delay

        Show(_pending);
    }

    internal static void Show(IInputComponent owner)
    {
        _pending = owner;
        EnsureBuilt();

        // The themed ToolTip control renders both a string (via its template's ContentPresenter) and a UI element as-is.
        _toolTip.Content = GetToolTip((AdamantiumComponent)owner);

        _popup.PlacementTarget = owner as UIComponent;
        _popup.Placement = GetPlacement((AdamantiumComponent)owner);
        _popup.DataContext = (owner as UIComponent)?.DataContext;   // so bindings in a content tooltip resolve
        _popup.IsOpen = true;
    }

    // EMPTIED as well as closed: the card is shared and lives for good, and what it last showed - the tip and the
    // element it was for - would live as long.
    internal static void Hide()
    {
        _timer?.Stop();
        _pending = null;

        if (_popup == null) return;

        _popup.IsOpen = false;
        _popup.PlacementTarget = null;
        _popup.DataContext = null;
        _toolTip.Content = null;

        // MEASURED to hand the tip back: a presenter lets its content go as it is measured, and a closed card never is.
        _toolTip.Measure(new Adamantium.Mathematics.Size(0, 0), force: true);
    }

    // An element thrown away while its tip is up takes the tip down: nothing else would, and the card would go on
    // showing what has been let go of.
    private static void OnDiscarded(ReadOnlySpan<IFundamentalUIComponent> gone)
    {
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
        _popup = new Popup { Child = _toolTip, VerticalOffset = 4 };

        _timer = new DispatcherTimer();
        _timer.Tick += OnTimerTick;

        DiscardedVisuals.Discarded += OnDiscarded;
    }
}
