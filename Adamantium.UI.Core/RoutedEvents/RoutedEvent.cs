using System.Diagnostics;
using Adamantium.Core.TypeParsing;
using Adamantium.UI.Core.Diagnostics;
using Adamantium.UI.Core.TypeParsers;

namespace Adamantium.UI.Core.RoutedEvents;

// [TypeParser] so a markup attribute string (EventTrigger Event="Loaded") resolves to the registered event, like Brush
// resolves via BrushParser.
[TypeParser(typeof(RoutedEventParser))]
public sealed class RoutedEvent
{
   private static Int32 globalIndex = 1;
   private static readonly object ReportLock = new();
   private static readonly HashSet<(Type Observer, Type Fault)> ReportedObserverFaults = [];

   private readonly object registrationLock = new();
   private volatile ClassHandlerTable classHandlers = new([]);
   private volatile IRoutedEventObserver[] observers;

   public Int32 GlobalIndex { get;}
   public String Name { get; private set; }
   public RoutingStrategy RoutingStrategy { get; private set; }
   public Type EventHandlerType { get; private set; }
   public Type EventOwnerType { get; private set; }

   internal RoutedEvent(String name, RoutingStrategy routingRoutingStrategy, Type eventHandlerType, Type eventOwnerType)
   {
      GlobalIndex = globalIndex++;
      Name = name;
      RoutingStrategy = routingRoutingStrategy;
      EventHandlerType = eventHandlerType;
      EventOwnerType = eventOwnerType;
   }

   /// <summary>Whether ANY class handler is registered for this event. Read on every raise to answer "could this be heard
   /// at all"; only ever goes from false to true (there is no unregister).</summary>
   public bool HasClassHandlers => classHandlers.All.Length != 0;

   internal IRoutedEventObserver[] Observers => observers;

   public void RegisterClassHandler(Type classType, Delegate handler, Boolean handledEventsToo = false)
   {
      lock (registrationLock)
      {
         var subscription = new ClassEventSubscription
         {
            HandledEventsToo = handledEventsToo,
            Handler = handler,
            TargetType = classType
         };

         classHandlers = new ClassHandlerTable([.. classHandlers.All, subscription]);
      }
   }

   public void RegisterClassHandler<T>(Delegate handler, Boolean handledEventsToo = false)
   {
      RegisterClassHandler(typeof(T), handler, handledEventsToo);
   }

   /// <summary>Starts telling <paramref name="observer"/> about every raise of this event, wherever it is raised. While
   /// the event has no observer, a raise pays one check for this. Adding an observer twice adds it once. An observer is
   /// told on the raising thread - on several at once when the event is raised on several - and a raise under way when
   /// it is removed may still tell it. What an observer throws does not reach the raise; the first fault of each
   /// exception type an observer type throws is logged.</summary>
   public void AddObserver(IRoutedEventObserver observer)
   {
      ArgumentNullException.ThrowIfNull(observer);

      lock (registrationLock)
      {
         if (observers != null && Array.Exists(observers, o => ReferenceEquals(o, observer)))
         {
            return;
         }

         observers = [.. observers ?? [], observer];
      }
   }

   /// <summary>Stops telling <paramref name="observer"/> about this event.</summary>
   public void RemoveObserver(IRoutedEventObserver observer)
   {
      ArgumentNullException.ThrowIfNull(observer);

      lock (registrationLock)
      {
         if (observers == null || !Array.Exists(observers, o => ReferenceEquals(o, observer)))
         {
            return;
         }

         IRoutedEventObserver[] left = [.. observers.Where(o => !ReferenceEquals(o, observer))];
         observers = left.Length == 0 ? null : left;
      }
   }

   public void InvokeClassHandlers(object sender, RoutedEventArgs e)
   {
      InvokeClassHandlers(sender, e, null);
   }

   internal void InvokeClassHandlers(object sender, RoutedEventArgs e, IRoutedEventObserver[] watching)
   {
      foreach (var subscription in classHandlers.For(sender.GetType()))
      {
         Invoke(subscription, sender, e, true, watching);
      }
   }

   internal static void Invoke(EventSubscription subscription, object sender, RoutedEventArgs e, bool isClassHandler,
      IRoutedEventObserver[] watching)
   {
      if (watching != null)
      {
         InvokeObserved(subscription, sender, e, isClassHandler, watching);
      }
      else if (!e.Handled || subscription.HandledEventsToo)
      {
         subscription.Invoke(sender, e);
      }
   }

   internal static void TellRaised(IRoutedEventObserver[] watching, IObservableComponent source, RoutedEventArgs e)
   {
      foreach (var observer in watching)
      {
         try
         {
            observer.OnRaised(source, e);
         }
         catch (Exception fault)
         {
            Report(observer, fault);
         }
      }
   }

   internal static void TellFinished(IRoutedEventObserver[] watching, IObservableComponent source, RoutedEventArgs e)
   {
      foreach (var observer in watching)
      {
         try
         {
            observer.OnRouteFinished(source, e);
         }
         catch (Exception fault)
         {
            Report(observer, fault);
         }
      }
   }

   public override int GetHashCode()
   {
      return GlobalIndex;
   }

   public override string ToString()
   {
      return GetType().Name +"."+Name;
   }

   private static void InvokeObserved(EventSubscription subscription, object sender, RoutedEventArgs e,
      bool isClassHandler, IRoutedEventObserver[] watching)
   {
      if (e.Handled && !subscription.HandledEventsToo)
      {
         Tell(watching, Call(subscription, sender, e, isClassHandler, true, TimeSpan.Zero, null, true));
         return;
      }

      var wasHandled = e.Handled;
      var started = Stopwatch.GetTimestamp();
      try
      {
         subscription.Invoke(sender, e);
      }
      catch (Exception thrown)
      {
         Tell(watching, Call(subscription, sender, e, isClassHandler, wasHandled, Stopwatch.GetElapsedTime(started),
            thrown, false));
         throw;
      }

      Tell(watching, Call(subscription, sender, e, isClassHandler, wasHandled, Stopwatch.GetElapsedTime(started), null,
         false));
   }

   private static RoutedEventHandlerCall Call(EventSubscription subscription, object sender, RoutedEventArgs e,
      bool isClassHandler, bool wasHandled, TimeSpan elapsed, Exception thrown, bool skipped)
   {
      return new RoutedEventHandlerCall
      {
         Element = sender as IObservableComponent,
         Handler = subscription.Handler,
         IsClassHandler = isClassHandler,
         HandledEventsToo = subscription.HandledEventsToo,
         WasHandled = wasHandled,
         IsHandled = e.Handled,
         Skipped = skipped,
         Exception = thrown,
         Args = e,
         Elapsed = elapsed,
      };
   }

   private static void Tell(IRoutedEventObserver[] watching, in RoutedEventHandlerCall call)
   {
      foreach (var observer in watching)
      {
         try
         {
            observer.OnHandlerInvoked(in call);
         }
         catch (Exception fault)
         {
            Report(observer, fault);
         }
      }
   }

   private static void Report(IRoutedEventObserver observer, Exception fault)
   {
      lock (ReportLock)
      {
         if (!ReportedObserverFaults.Add((observer.GetType(), fault.GetType())))
         {
            return;
         }
      }

      Serilog.Log.Logger.Warning(fault, "Routed event observer {Observer} threw", observer.GetType().FullName);
   }
}
