namespace Adamantium.UI.Core.RoutedEvents;

public class EventSubscription
{
   public Delegate Handler { get; set; }

   public bool HandledEventsToo { get; set; }

   internal void Invoke(object sender, RoutedEventArgs e)
   {
      e.Invoke(Handler, sender);
   }
}
