using Adamantium.Mathematics;

namespace Adamantium.UI.Controls.Primitives;

public class DragCompletedEventArgs:DragEventArgs
{
   public bool IsCanceled { get; }
   public DragCompletedEventArgs(Vector2<double> changedPoint, bool isCanceled) : base(changedPoint)
   {
      IsCanceled = isCanceled;
   }
}