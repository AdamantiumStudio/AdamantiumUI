using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Core.Input;

/// <summary>Gestures made of pointer events.</summary>
public static class Gestures
{
    /// <summary>A finger or a pen held still on an element: <see cref="HoldingState.Started"/> once it has rested
    /// <see cref="HoldingTime"/> without moving past the drag threshold, then <see cref="HoldingState.Completed"/> as
    /// it lifts, or <see cref="HoldingState.Canceled"/> if it moves off or the system takes it away first. A lift that
    /// ends a hold is no tap: what acts on the lift - a button's click - asks <see cref="Pointer.IsHolding"/> and does
    /// not happen. The capture stays, so a thumb held a moment before it moves still drags. The mouse does not hold,
    /// and neither does a pen pressed with its side button, which is already the right button.</summary>
    public static readonly RoutedEvent HoldingEvent = EventManager.RegisterRoutedEvent("Holding",
        RoutingStrategy.Bubble, typeof(HoldingEventHandler), typeof(Gestures));

    /// <summary>How long a contact rests before it holds.</summary>
    public static TimeSpan HoldingTime { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Two fingers or more on an element that takes gestures (<see cref="IsManipulationEnabledProperty"/>),
    /// recognized as a pan, a pinch, a rotation or any of them together - <see cref="ManipulationEventArgs.Recognized"/>
    /// says which. <see cref="ManipulationState.Started"/> comes as the first part is recognized; until then the
    /// fingers are plain pointers. From then on they are the gesture's: what they pressed is told
    /// <see cref="PointerEvents.PointerCanceledEvent"/> and loses them, so a button under one is not clicked, and their
    /// moves and lifts go to no element. A finger that lands on the element later joins it. It goes on while one finger
    /// is left, and <see cref="ManipulationState.Completed"/> comes with the last lift.</summary>
    public static readonly RoutedEvent ManipulationEvent = EventManager.RegisterRoutedEvent("Manipulation",
        RoutingStrategy.Bubble, typeof(ManipulationEventHandler), typeof(Gestures));

    /// <summary>Whether gestures of fingers are made on this element, and not on what it holds: the nearest one that
    /// says so to the first finger gets them. Off by default; a zoom box, a canvas or a 3D view turns it on.</summary>
    public static readonly AdamantiumProperty IsManipulationEnabledProperty = AdamantiumProperty.RegisterAttached(
        "IsManipulationEnabled", typeof(bool), typeof(AdamantiumComponent), new PropertyMetadata(false));

    public static bool GetIsManipulationEnabled(IAdamantiumComponent element) =>
        element.GetValue<bool>(IsManipulationEnabledProperty);

    public static void SetIsManipulationEnabled(IAdamantiumComponent element, bool value) =>
        element.SetValue(IsManipulationEnabledProperty, value);

    /// <summary>How far, in an element's units, fingers spread or close before they pinch.</summary>
    public static double PinchThreshold { get; set; } = 12;

    /// <summary>How far, in degrees, fingers turn before they rotate.</summary>
    public static double RotationThreshold { get; set; } = 8;
}
