using Adamantium.Mathematics;

namespace Adamantium.UI.Core.RoutedEvents;

public class SizeChangedEventArgs:RoutedEventArgs
{
   public Size NewSize { get; private set; }
   public Size OldSize { get; private set; }
   public Boolean WidthChanged { get; private set; }
   public Boolean HeightChanged { get; private set; }

   public SizeChangedEventArgs(Size oldSize, Size newSize, bool widthChanged, bool heightChanged)
   {
      NewSize = newSize;
      OldSize = oldSize;
      WidthChanged = widthChanged;
      HeightChanged = heightChanged;
   }

   protected override void InvokeEventHandler(Delegate handler, object target)
   {
      if (handler is SizeChangedEventHandler typed)
      {
         typed(target, this);
         return;
      }

      base.InvokeEventHandler(handler, target);
   }
}