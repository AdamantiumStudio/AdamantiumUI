using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Core.Diagnostics;

/// <summary>Watches the raises of the routed events it is added to with <see cref="RoutedEvent.AddObserver"/>: a
/// debugging tool's view of input and of every other routed event. Called inside the raise, on the raising thread (see
/// <see cref="RoutedEvent.AddObserver"/>), so it must be quick and must not change the args.</summary>
public interface IRoutedEventObserver
{
    /// <summary>The event is about to travel its route from <paramref name="source"/>. Told even when nothing on the
    /// route listens.</summary>
    void OnRaised(IObservableComponent source, RoutedEventArgs e);

    /// <summary>A handler on the route has run, has thrown, or was skipped as the event was already handled.</summary>
    void OnHandlerInvoked(in RoutedEventHandlerCall call);

    /// <summary>The route has ended: walked to its end, or cut short by a handler that threw.</summary>
    void OnRouteFinished(IObservableComponent source, RoutedEventArgs e);
}
