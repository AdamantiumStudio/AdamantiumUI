using Adamantium.Mathematics;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.Primitives;

public class DragEventArgs:RoutedEventArgs
{
   public Vector2<double> Change { get; }

   public DragEventArgs(Vector2<double> changedPoint)
   {
      Change = changedPoint;
   }
}