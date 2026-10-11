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
}
