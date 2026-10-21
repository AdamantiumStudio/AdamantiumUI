namespace Adamantium.UI.Core.RoutedEvents;

public class EventSubscription
{
   private HandlerCall call;

   public Delegate Handler { get; set; }

   public bool HandledEventsToo { get; set; }

   internal void Invoke(object sender, RoutedEventArgs e)
   {
      var handler = Handler;
      var known = call;
      if (known == null || known.DelegateType != handler.GetType())
      {
         known = RoutedEventInvokers.For(handler.GetType());
         call = known;
      }

      known.Invoke(handler, sender, e);
   }
}
