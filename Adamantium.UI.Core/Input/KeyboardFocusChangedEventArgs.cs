using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Core.Input;

public class KeyboardFocusChangedEventArgs:RoutedEventArgs
{
   public IInputComponent OldFocus { get; private set; }
   public IInputComponent NewFocus { get; private set; }

   public KeyboardFocusChangedEventArgs(IInputComponent oldFocus, IInputComponent newFocus)
   {
      OldFocus = oldFocus;
      NewFocus = newFocus;
   }

   protected override void InvokeEventHandler(Delegate handler, object target)
   {
      if (handler is KeyboardFocusChangedEventHandler typed)
      {
         typed(target, this);
         return;
      }

      base.InvokeEventHandler(handler, target);
   }
}