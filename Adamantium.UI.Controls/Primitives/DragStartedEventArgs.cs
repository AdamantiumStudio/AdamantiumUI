using Adamantium.Mathematics;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.Primitives;

public class DragStartedEventArgs:RoutedEventArgs
{
   public Vector2 Offset { get; }

   public DragStartedEventArgs(Vector2 offset)
   {
      Offset = offset;
   }

   protected override void InvokeEventHandler(Delegate handler, object target)
   {
      if (handler is DragStartedEventHandler typed)
      {
         typed(target, this);
         return;
      }

      base.InvokeEventHandler(handler, target);
   }
}