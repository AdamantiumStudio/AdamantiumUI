namespace Adamantium.UI.Core.RoutedEvents;

public class EventSubscription
{
   private HandlerInvoker invoker;

   public Delegate Handler { get; set; }

   public bool HandledEventsToo { get; set; }

   internal void Invoke(object sender, RoutedEventArgs e)
   {
      var handler = Handler;
      var call = invoker;
      if (call == null || call.HandlerType != handler.GetType())
      {
         call = HandlerInvoker.For(handler.GetType());
         invoker = call;
      }

      call.Invoke(handler, sender, e);
   }
}
