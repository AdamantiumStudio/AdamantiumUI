using Adamantium.Mathematics;

namespace Adamantium.UI.Core.RoutedEvents;

public class SizeChangedEventArgs:RoutedEventArgs
{
   public Size<double> NewSize { get; private set; }
   public Size<double> OldSize { get; private set; }
   public Boolean WidthChanged { get; private set; }
   public Boolean HeightChanged { get; private set; }

   public SizeChangedEventArgs(Size<double> oldSize, Size<double> newSize, bool widthChanged, bool heightChanged)
   {
      NewSize = newSize;
      OldSize = oldSize;
      WidthChanged = widthChanged;
      HeightChanged = heightChanged;
   }

}