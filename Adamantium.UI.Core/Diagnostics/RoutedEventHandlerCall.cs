using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Core.Diagnostics;

/// <summary>One handler on a routed event's route, as an <see cref="IRoutedEventObserver"/> is told it: run, skipped or
/// thrown.</summary>
public readonly struct RoutedEventHandlerCall
{
    /// <summary>The element of the route the handler is on.</summary>
    public IObservableComponent Element { get; init; }

    /// <summary>The handler as it was added.</summary>
    public Delegate Handler { get; init; }

    /// <summary>A class handler (<see cref="RoutedEvent.RegisterClassHandler(Type, Delegate, bool)"/>) rather than an
    /// element's own.</summary>
    public bool IsClassHandler { get; init; }

    /// <summary>The handler asked to run on events already handled.</summary>
    public bool HandledEventsToo { get; init; }

    /// <summary>Whether the event was handled before the call.</summary>
    public bool WasHandled { get; init; }

    /// <summary>Whether the event was handled right after the call: with <see cref="WasHandled"/> false, this handler
    /// handled it.</summary>
    public bool IsHandled { get; init; }

    /// <summary>The handler did not run: the event was already handled and it did not ask for handled events.</summary>
    public bool Skipped { get; init; }

    /// <summary>What the handler threw, if it threw; the raise goes on throwing it and the route ends there.</summary>
    public Exception Exception { get; init; }

    /// <summary>The args of the raise - the live object, which the rest of the route goes on changing.</summary>
    public RoutedEventArgs Args { get; init; }

    /// <summary>How long the handler ran.</summary>
    public TimeSpan Elapsed { get; init; }
}
