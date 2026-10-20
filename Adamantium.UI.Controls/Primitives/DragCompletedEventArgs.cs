using Adamantium.Mathematics;

namespace Adamantium.UI.Controls.Primitives;

public class DragCompletedEventArgs:DragEventArgs
{
   public bool IsCanceled { get; }
   public DragCompletedEventArgs(Vector2 changedPoint, bool isCanceled) : base(changedPoint)
   {
      IsCanceled = isCanceled;
   }

   protected override void InvokeEventHandler(Delegate handler, object target)
   {
      if (handler is DragCompletedEventHandler typed)
      {
         typed(target, this);
         return;
      }

      base.InvokeEventHandler(handler, target);
   }
}