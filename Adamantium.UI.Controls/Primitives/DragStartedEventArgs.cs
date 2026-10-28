using Adamantium.Mathematics;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.Primitives;

public class DragStartedEventArgs:RoutedEventArgs
{
   public Vector2<double> Offset { get; }

   public DragStartedEventArgs(Vector2<double> offset)
   {
      Offset = offset;
   }
}