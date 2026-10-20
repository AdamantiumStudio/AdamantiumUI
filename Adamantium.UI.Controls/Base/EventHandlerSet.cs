using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.Base;

internal sealed class EventHandlerSet
{
    public EventHandlerSet(RoutedEvent routedEvent, EventSubscription[] subscriptions)
    {
        RoutedEvent = routedEvent;
        Subscriptions = subscriptions;
    }

    public RoutedEvent RoutedEvent { get; }

    public EventSubscription[] Subscriptions { get; }
}
