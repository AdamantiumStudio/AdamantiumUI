using Adamantium.Mathematics;

namespace Adamantium.UI.Controls.Primitives;

public class DragCompletedEventArgs:DragEventArgs
{
   public bool IsCanceled { get; }
   public DragCompletedEventArgs(Vector2 changedPoint, bool isCanceled) : base(changedPoint)
   {
      IsCanceled = isCanceled;
   }
}