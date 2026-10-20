using Adamantium.Mathematics;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.Primitives;

public class DragEventArgs:RoutedEventArgs
{
   public Vector2 Change { get; }

   public DragEventArgs(Vector2 changedPoint)
   {
      Change = changedPoint;
   }

   protected override void InvokeEventHandler(Delegate handler, object target)
   {
      if (handler is DragEventHandler typed)
      {
         typed(target, this);
         return;
      }

      base.InvokeEventHandler(handler, target);
   }
}