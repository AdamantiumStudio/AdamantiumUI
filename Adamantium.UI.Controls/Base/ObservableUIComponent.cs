using Adamantium.UI.Core;
using Adamantium.UI.Core.Diagnostics;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.Base;

public class ObservableUIComponent : UIComponent, IObservableComponent
{
    private readonly object handlersLock = new();
    private volatile EventHandlerSet[] eventHandlers = [];

    public IObservableComponent ObservableParent => ((IUIComponent)this).VisualParent as IObservableComponent;

    /// <summary>Adds a handler for <paramref name="routedEvent"/>. Added by a handler of this element during a raise, it
    /// runs from the next raise on; added to an element the route has yet to reach, it runs in this one.</summary>
    public void AddHandler(RoutedEvent routedEvent, Delegate handler, bool handledEventsToo = false)
    {
        ArgumentNullException.ThrowIfNull(routedEvent);
        ArgumentNullException.ThrowIfNull(handler);

        var subscription = new EventSubscription
        {
            Handler = handler,
            HandledEventsToo = handledEventsToo,
        };

        lock (handlersLock)
        {
            var sets = eventHandlers;
            var index = IndexOf(sets, routedEvent);
            if (index < 0)
            {
                eventHandlers = [.. sets, new EventHandlerSet(routedEvent, [subscription])];
                return;
            }

            EventHandlerSet[] changed = [.. sets];
            changed[index] = new EventHandlerSet(routedEvent, [.. sets[index].Subscriptions, subscription]);
            eventHandlers = changed;
        }
    }

    public void AddHandler(RoutedEvent routedEvent, RoutedEventHandler handler, bool handledEventsToo = false)
    {
        AddHandler(routedEvent, (Delegate)handler, handledEventsToo);
    }

    public void RaiseEvent(RoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(e.RoutedEvent);

        // Source/OriginalSource are assigned before the "anyone listening" check, so an args object the caller keeps
        // reads the same whether or not the route was walked.
        e.Source ??= this;
        e.OriginalSource ??= this;

        var watching = e.RoutedEvent.Observers;
        if (watching == null)
        {
            if (WouldBeHeard(e.RoutedEvent))
            {
                Route(e, null);
            }

            return;
        }

        RoutedEvent.TellRaised(watching, this, e);
        try
        {
            Route(e, watching);
        }
        finally
        {
            RoutedEvent.TellFinished(watching, this, e);
        }
    }

    public IEnumerable<IObservableComponent> GetBubbleEventRoute()
    {
        var element = (IObservableComponent)this;
        while (element != null)
        {
            yield return element;
            element = element.ObservableParent;
        }
    }

    public IEnumerable<IObservableComponent> GetTunnelEventRoute()
    {
        return GetBubbleEventRoute().Reverse();
    }

    /// <summary>Removes every subscription of <paramref name="handler"/> to <paramref name="routedEvent"/>. Removed by a
    /// handler of this element during a raise, it still runs in that raise; removed from an element the route has yet
    /// to reach, it does not.</summary>
    public void RemoveHandler(RoutedEvent routedEvent, Delegate handler)
    {
        ArgumentNullException.ThrowIfNull(routedEvent);
        ArgumentNullException.ThrowIfNull(handler);

        lock (handlersLock)
        {
            var sets = eventHandlers;
            var index = IndexOf(sets, routedEvent);
            if (index < 0)
            {
                return;
            }

            EventSubscription[] left = [.. sets[index].Subscriptions.Where(s => s.Handler != handler)];
            if (left.Length == sets[index].Subscriptions.Length)
            {
                return;
            }

            if (left.Length > 0)
            {
                EventHandlerSet[] changed = [.. sets];
                changed[index] = new EventHandlerSet(routedEvent, left);
                eventHandlers = changed;
                return;
            }

            eventHandlers = [.. sets.Where(s => s.RoutedEvent != routedEvent)];
        }
    }

    /// <summary>Whether raising <paramref name="routedEvent"/> here would reach any handler or observer, so callers can
    /// skip building args nobody reads. Takes no lock and allocates nothing.</summary>
    protected bool WouldBeHeard(RoutedEvent routedEvent)
    {
        if (routedEvent == null)
        {
            return false;
        }

        if (routedEvent.HasClassHandlers || routedEvent.Observers != null)
        {
            return true;
        }

        if (routedEvent.RoutingStrategy == RoutingStrategy.Direct)
        {
            return OwnHandlers(routedEvent) != null;
        }

        // Bubble and Tunnel travel the SAME set of elements, in opposite directions - so for "is anyone on it", one walk
        // answers both.
        for (var element = (IObservableComponent)this; element != null; element = element.ObservableParent)
        {
            if (element is ObservableUIComponent component && component.OwnHandlers(routedEvent) != null)
            {
                return true;
            }
        }

        return false;
    }

    private void Route(RoutedEventArgs e, IRoutedEventObserver[] watching)
    {
        switch (e.RoutedEvent.RoutingStrategy)
        {
            case RoutingStrategy.Direct:
                RaiseDirectEvent(e, watching);
                break;
            case RoutingStrategy.Bubble:
                RaiseBubbleEvent(e, watching);
                break;
            case RoutingStrategy.Tunnel:
                RaiseTunnelEvent(this, e, watching);
                break;
        }
    }

    private void RaiseDirectEvent(RoutedEventArgs e, IRoutedEventObserver[] watching)
    {
        var routedEvent = e.RoutedEvent;
        if (routedEvent.HasClassHandlers)
        {
            routedEvent.InvokeClassHandlers(this, e, watching);
        }

        var subscriptions = OwnHandlers(routedEvent);
        if (subscriptions == null)
        {
            return;
        }

        foreach (var subscription in subscriptions)
        {
            RoutedEvent.Invoke(subscription, this, e, false, watching);
        }
    }

    private void RaiseBubbleEvent(RoutedEventArgs e, IRoutedEventObserver[] watching)
    {
        for (var node = (IObservableComponent)this; node != null; node = node.ObservableParent)
        {
            var element = (ObservableUIComponent)node;
            e.Source = element;
            element.RaiseDirectEvent(e, watching);
        }
    }

    private static void RaiseTunnelEvent(IObservableComponent node, RoutedEventArgs e, IRoutedEventObserver[] watching)
    {
        var parent = node.ObservableParent;
        if (parent != null)
        {
            RaiseTunnelEvent(parent, e, watching);
        }

        var element = (ObservableUIComponent)node;
        e.Source = element;
        element.RaiseDirectEvent(e, watching);
    }

    private EventSubscription[] OwnHandlers(RoutedEvent routedEvent)
    {
        foreach (var set in eventHandlers)
        {
            if (set.RoutedEvent == routedEvent)
            {
                return set.Subscriptions;
            }
        }

        return null;
    }

    private static int IndexOf(EventHandlerSet[] sets, RoutedEvent routedEvent)
    {
        for (var i = 0; i < sets.Length; i++)
        {
            if (sets[i].RoutedEvent == routedEvent)
            {
                return i;
            }
        }

        return -1;
    }
}
