namespace Adamantium.UI.Core.Input;

public class KeyboardGotFocusEventArgs: KeyboardFocusChangedEventArgs
{
   public InputModifiers Modifiers { get; private set; }
   public NavigationMethod NavigationMethod { get; private set; }

   public KeyboardGotFocusEventArgs(IInputComponent oldFocus, IInputComponent newFocus, NavigationMethod navigationMethod, InputModifiers modifiers) : base(oldFocus, newFocus)
   {
      Modifiers = modifiers;
      NavigationMethod = navigationMethod;
   }

   protected override void InvokeEventHandler(Delegate handler, object target)
   {
      if (handler is KeyboardGotFocusEventHandler typed)
      {
         typed(target, this);
         return;
      }

      base.InvokeEventHandler(handler, target);
   }
}