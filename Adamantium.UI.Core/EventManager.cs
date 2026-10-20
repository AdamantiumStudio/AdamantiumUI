using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Core;

public static class EventManager
{
   private static Dictionary<Type, List<RoutedEvent>> routedEvents = new Dictionary<Type, List<RoutedEvent>>();

   private static Dictionary<RoutedEvent, List<ClassEventSubscription>> classEventSubscriptions =
      new Dictionary<RoutedEvent, List<ClassEventSubscription>>();

   /// <summary>A routed event has just been registered: a debugging tool that watches every event adds its observer
   /// here to the events whose owners load after it started - subscribing first, then going over
   /// <see cref="GetRoutedEvents()"/>. Raised inside the static initializer of the event's owner, on the thread that
   /// runs it, while the owner's field for the event is still unset; what a subscriber throws is logged and does not
   /// reach the owner.</summary>
   public static event Action<RoutedEvent> RoutedEventRegistered;

   /// <summary>Every routed event registered so far.</summary>
   public static RoutedEvent[] GetRoutedEvents()
   {
      lock (routedEvents)
      {
         return [.. routedEvents.Values.SelectMany(events => events)];
      }
   }

   public static RoutedEvent[] GetRoutedEvents(Type ownerType)
   {
      lock (routedEvents)
      {
         if (routedEvents.ContainsKey(ownerType))
         {
            return routedEvents[ownerType].ToArray();
         }
      }
      return null;
   }

   public static RoutedEvent RegisterRoutedEvent(String name, RoutingStrategy routingStrategy, Type eventHandlerType, Type eventOwnerType)
   {
      var routedEvent = Register(name, routingStrategy, eventHandlerType, eventOwnerType);
      if (RoutedEventRegistered is { } registered)
      {
         foreach (Action<RoutedEvent> subscriber in registered.GetInvocationList())
         {
            try
            {
               subscriber(routedEvent);
            }
            catch (Exception fault)
            {
               Serilog.Log.Logger.Warning(fault, "RoutedEventRegistered subscriber threw for {Event}", routedEvent);
            }
         }
      }

      return routedEvent;
   }

   private static RoutedEvent Register(String name, RoutingStrategy routingStrategy, Type eventHandlerType, Type eventOwnerType)
   {
      lock (routedEvents)
      {
         if (String.IsNullOrEmpty(name))
         {
            throw new ArgumentNullException(nameof(name));
         }

         if (eventHandlerType == null)
         {
            throw new ArgumentNullException(nameof(eventHandlerType));
         }

         if (eventOwnerType == null)
         {
            throw new ArgumentNullException(nameof(eventOwnerType));
         }

         RoutedEvent routedEvent = null;

         if (routedEvents.ContainsKey(eventOwnerType))
         {
            List<RoutedEvent> eventsList = routedEvents[eventOwnerType];
            foreach (var @event in eventsList)
            {
               if (@event.Name == name)
               {
                  throw new ArgumentException(eventOwnerType.Name + " class already contains routed event with name "+ name );
               }
            }
            routedEvent = new RoutedEvent(name, routingStrategy, eventHandlerType, eventOwnerType);
            eventsList.Add(routedEvent);
         }
         else
         {
            routedEvent = new RoutedEvent(name, routingStrategy, eventHandlerType, eventOwnerType);
            routedEvents.Add(eventOwnerType, new List<RoutedEvent>() {routedEvent});
         }
            

         return routedEvent;
      }
   }

   /// <summary>Resolves a routed event by markup name, bare (first owner wins) or <c>Owner.Event</c>; null if none is
   /// registered.</summary>
   public static RoutedEvent FindRoutedEvent(String name)
   {
      if (String.IsNullOrEmpty(name)) return null;

      String ownerName = null, eventName = name;
      var dot = name.LastIndexOf('.');
      if (dot >= 0)
      {
         ownerName = name.Substring(0, dot);
         eventName = name.Substring(dot + 1);
      }

      lock (routedEvents)
      {
         foreach (var pair in routedEvents)
         {
            if (ownerName != null && pair.Key.Name != ownerName) continue;
            foreach (var routedEvent in pair.Value)
               if (routedEvent.Name == eventName) return routedEvent;
         }
      }

      return null;
   }

   public static void RegisterClassHandler(Type classType, RoutedEvent routedEvent, Delegate handler, Boolean handledEventsToo = false)
   {
      routedEvent.RegisterClassHandler(classType, handler, handledEventsToo);
   }

   public static void RegisterClassHandler<T>(RoutedEvent routedEvent, Delegate handler, Boolean handledEventsToo = false)
   {
      routedEvent.RegisterClassHandler<T>(handler, handledEventsToo);
   }
}