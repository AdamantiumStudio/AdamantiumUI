using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Core.Input;

/// <summary>The routed events every pointer raises - the mouse, a finger or a pen alike.</summary>
public static class PointerEvents
{
    public static readonly RoutedEvent PreviewPointerPressedEvent = EventManager.RegisterRoutedEvent("PreviewPointerPressed",
        RoutingStrategy.Tunnel, typeof(PointerPressedEventHandler), typeof(PointerEvents));

    public static readonly RoutedEvent PointerPressedEvent = EventManager.RegisterRoutedEvent("PointerPressed",
        RoutingStrategy.Bubble, typeof(PointerPressedEventHandler), typeof(PointerEvents));

    public static readonly RoutedEvent PreviewPointerReleasedEvent = EventManager.RegisterRoutedEvent("PreviewPointerReleased",
        RoutingStrategy.Tunnel, typeof(PointerReleasedEventHandler), typeof(PointerEvents));

    public static readonly RoutedEvent PointerReleasedEvent = EventManager.RegisterRoutedEvent("PointerReleased",
        RoutingStrategy.Bubble, typeof(PointerReleasedEventHandler), typeof(PointerEvents));

    public static readonly RoutedEvent PreviewPointerMovedEvent = EventManager.RegisterRoutedEvent("PreviewPointerMoved",
        RoutingStrategy.Tunnel, typeof(PointerEventHandler), typeof(PointerEvents));

    public static readonly RoutedEvent PointerMovedEvent = EventManager.RegisterRoutedEvent("PointerMoved",
        RoutingStrategy.Bubble, typeof(PointerEventHandler), typeof(PointerEvents));

    /// <summary>Raised on each element that joins the chain under a pointer - the element hit and its ancestors.</summary>
    public static readonly RoutedEvent PointerEnteredEvent = EventManager.RegisterRoutedEvent("PointerEntered",
        RoutingStrategy.Direct, typeof(PointerEventHandler), typeof(PointerEvents));

    /// <summary>Raised on each element that leaves the chain under a pointer.</summary>
    public static readonly RoutedEvent PointerExitedEvent = EventManager.RegisterRoutedEvent("PointerExited",
        RoutingStrategy.Direct, typeof(PointerEventHandler), typeof(PointerEvents));

    /// <summary>Raised on the element that became the one directly under a pointer.</summary>
    public static readonly RoutedEvent DirectlyOverEnteredEvent = EventManager.RegisterRoutedEvent("DirectlyOverEntered",
        RoutingStrategy.Direct, typeof(PointerEventHandler), typeof(PointerEvents));

    /// <summary>Raised on the element that stopped being the one directly under a pointer.</summary>
    public static readonly RoutedEvent DirectlyOverExitedEvent = EventManager.RegisterRoutedEvent("DirectlyOverExited",
        RoutingStrategy.Direct, typeof(PointerEventHandler), typeof(PointerEvents));

    public static readonly RoutedEvent PreviewPointerWheelChangedEvent = EventManager.RegisterRoutedEvent(
        "PreviewPointerWheelChanged", RoutingStrategy.Tunnel, typeof(PointerWheelEventHandler), typeof(PointerEvents));

    public static readonly RoutedEvent PointerWheelChangedEvent = EventManager.RegisterRoutedEvent("PointerWheelChanged",
        RoutingStrategy.Bubble, typeof(PointerWheelEventHandler), typeof(PointerEvents));

    public static readonly RoutedEvent GotPointerCaptureEvent = EventManager.RegisterRoutedEvent("GotPointerCapture",
        RoutingStrategy.Bubble, typeof(PointerEventHandler), typeof(PointerEvents));

    /// <summary>Raised on the element that held a pointer's capture and no longer does - released, taken by another, or
    /// revoked by the OS.</summary>
    public static readonly RoutedEvent PointerCaptureLostEvent = EventManager.RegisterRoutedEvent("PointerCaptureLost",
        RoutingStrategy.Bubble, typeof(PointerEventHandler), typeof(PointerEvents));

    /// <summary>Raised where a pointer was pressed when the OS took it away mid-press - a system gesture claimed the
    /// finger. No release follows.</summary>
    public static readonly RoutedEvent PointerCanceledEvent = EventManager.RegisterRoutedEvent("PointerCanceled",
        RoutingStrategy.Bubble, typeof(PointerEventHandler), typeof(PointerEvents));
}
